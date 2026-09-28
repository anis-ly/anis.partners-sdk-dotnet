using System.Net;
using System.Security.Cryptography;
using Anis.Partners.Sdk.DependencyInjection;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Signing;
using Microsoft.Extensions.DependencyInjection;

namespace Anis.Partners.Sdk.Tests;

/// <summary>A host that retries every HTTP call must still send correctly signed requests.</summary>
/// <remarks>
/// Many hosts add automatic retries to every client they build — one line, and the default of the .NET
/// Aspire service template. Those defaults are registered before the SDK's own handlers, so they sit
/// OUTSIDE the signing handler and send the same request object through it a second time. Each attempt
/// must leave with exactly one fresh signature over its own nonce; two stacked signatures are refused.
/// </remarks>
public sealed class HostRetryTests
{
    private static readonly Guid Wallet = Guid.Parse("2f1c8a94-6d37-4e52-b8a1-0c9e5d3f7b26");
    private static readonly Guid Operation = Guid.Parse("9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34");

    private const string ResponseKeyId = "partner-response-signing/v1-active";

    [Fact]
    public async Task A_retry_handler_from_the_host_defaults_resends_one_fresh_signature_per_attempt()
    {
        using var requestKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var responseKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyId = Guid.NewGuid();

        var stub = new SignedResponseStub(responseKey, ResponseKeyId)
        {
            Status = HttpStatusCode.Created,
            SignedAt = DateTimeOffset.UtcNow,
            Body = $$"""{"operationId":"{{Operation:D}}","status":"completed","soldCards":[{"soldCardId":"4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046","voucher":"V","serialNumber":"S"}]}""",
        };

        var wire = new RecordingWire(stub, responseKey, ResponseKeyId);
        var services = new ServiceCollection();

        // What a host's service defaults do: one retry handler on every client, registered first.
        services.ConfigureHttpClientDefaults(http => http
            .AddHttpMessageHandler(() => new RetryOnceHandler())
            .ConfigurePrimaryHttpMessageHandler(() => wire));

        services
            .AddAnisPartners(options => options.Authority = new Uri("https://partners.anis.ly"))
            .WithSigner(EcdsaP256Signer.FromEcdsa(ExportImport(requestKey), keyId));

        await using var provider = services.BuildServiceProvider();
        var anis = provider.GetRequiredService<IAnisPartnersClient>();

        var outcome = await anis.Orders.CreateAsync(Wallet, Operation, new CreateOrderRequest
        {
            CardId = Guid.Parse("8d4b1e73-9a25-4c60-8f37-6b2e9d5a1c48"),
            Quantity = 1,
            ExpectedUnitPrice = new Money(10.500m, "LYD"),
            ExpectedTotal = new Money(10.500m, "LYD"),
        }, TestContext.Current.CancellationToken);

        Assert.IsType<OrderCompleted>(outcome);

        var attempts = wire.Requests;
        Assert.Equal(2, attempts.Count);

        foreach (var attempt in attempts)
        {
            // One of each — a second value on any of them is a request Anis refuses.
            foreach (var header in new[] { "Signature", "Signature-Input", "Nonce", "X-Anis-Date", "Idempotency-Key", "Content-Digest" })
                Assert.True(attempt.Headers.TryGetValue(header, out var values) && values.Length == 1, $"{header}: {Describe(attempt, header)}");

            Assert.True(SignatureVerifies(attempt, requestKey), "each attempt's signature verifies over its own headers");
            Assert.Equal(Operation.ToString("D"), attempt.Headers["Idempotency-Key"][0]);
        }

        // A fresh nonce each time: resending the first attempt's bytes would be refused as a replay.
        Assert.NotEqual(attempts[0].Headers["Nonce"][0], attempts[1].Headers["Nonce"][0]);
    }

    private static ECDsa ExportImport(ECDsa key)
    {
        var copy = ECDsa.Create();
        copy.ImportParameters(key.ExportParameters(includePrivateParameters: true));

        return copy;
    }

    private static string Describe(WireRequest attempt, string header)
        => attempt.Headers.TryGetValue(header, out var values) ? $"{values.Length} value(s)" : "absent";

    // Rebuilds the order profile's base from exactly what went on the wire and checks the signature.
    private static bool SignatureVerifies(WireRequest attempt, ECDsa key)
    {
        var parameters = attempt.Headers["Signature-Input"][0]["sig1=".Length..];
        var signature = attempt.Headers["Signature"][0];

        var inputs = new SignatureInputs
        {
            Method = "POST",
            Authority = "partners.anis.ly",
            Path = $"/v1/wallets/{Wallet:D}/orders",
            CanonicalQuery = string.Empty,
            AnisDate = attempt.Headers["X-Anis-Date"][0],
            ContentDigest = attempt.Headers["Content-Digest"][0],
            Nonce = attempt.Headers["Nonce"][0],
            IdempotencyKey = Guid.Parse(attempt.Headers["Idempotency-Key"][0]),
        };

        var signatureBase = PartnerRequestSignatureBase.Build(SignatureProfiles.OrderMutation, inputs, parameters);
        var bytes = Convert.FromBase64String(signature["sig1=:".Length..^1]);

        return key.VerifyData(signatureBase, bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>A host retry policy reduced to its essence: send, discard the answer, send the same request again.</summary>
    private sealed class RetryOnceHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post)
                return await base.SendAsync(request, cancellationToken);

            (await base.SendAsync(request, cancellationToken)).Dispose();

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
