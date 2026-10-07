using System.Net;
using System.Text;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Tests;

/// <summary>What the pipeline actually puts on the wire, and what it refuses to hand back.</summary>
public sealed class PipelineBehaviourTests
{
    private static readonly Guid Wallet = Guid.Parse("2f1c8a94-6d37-4e52-b8a1-0c9e5d3f7b26");
    private static readonly Guid SoldCard = Guid.Parse("4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046");
    private static readonly Guid Invoice = Guid.Parse("c1a7e2d9-5b64-4f18-9e03-2d7a6c4b8f51");

    [Fact]
    public async Task A_safe_read_carries_a_signature_and_no_nonce_or_digest()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Body = """{"partner":{"id":"7c9e6679-7425-40de-944b-e07fc1f90ae7"},"application":{"id":"16fd2706-8baf-433b-82eb-8c7fada847da","scopes":["profile:read"]}}""";

        await fixture.Client.Profile.GetAsync(TestContext.Current.CancellationToken);

        var request = fixture.Stub.LastRequest!;

        Assert.True(request.Headers.Contains("Signature"));
        Assert.True(request.Headers.Contains("Signature-Input"));
        Assert.True(request.Headers.Contains("X-Anis-Date"));
        Assert.False(request.Headers.Contains("Nonce"));
        Assert.False(request.Headers.Contains("Idempotency-Key"));

        // A read carries no ;nonce parameter either: Anis refuses one on a route that forbids a nonce.
        Assert.DoesNotContain(";nonce=", request.Headers.GetValues("Signature-Input").Single(), StringComparison.Ordinal);
        Assert.Null(request.Content);
    }

    // The SHA-256 of zero bytes, and of the two bytes {}.
    private const string EmptyDigest = "sha-256=:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=:";
    private const string EmptyObjectDigest = "sha-256=:RBNvo1WzZ4oRRq0W9+hknpT7T8If536DEMBg9hyq/4o=:";

    [Fact]
    public async Task A_reveal_transmits_zero_body_bytes_and_digests_them()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.NoStore = true;
        fixture.Stub.Body = $$"""{"soldCardId":"{{SoldCard:D}}","voucher":"1234-5678"}""";

        await fixture.Client.OwnedCards.RevealAsync(Wallet, SoldCard, TestContext.Current.CancellationToken);

        AssertBodylessReveal(fixture);
    }

    [Fact]
    public async Task An_invoice_reveal_transmits_zero_body_bytes_and_digests_them()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.NoStore = true;
        fixture.Stub.Body = $$"""{"items":[{"soldCardId":"{{SoldCard:D}}","voucher":"1234-5678"}]}""";

        var revealed = await fixture.Client.OwnedCards.RevealInvoiceAsync(Wallet, Invoice, TestContext.Current.CancellationToken);

        Assert.Single(revealed.Items);
        Assert.Equal(
            $"/v1/wallets/{Wallet:D}/invoices/{Invoice:D}/cards/reveal",
            fixture.Stub.LastRequest!.RequestUri!.AbsolutePath);
        AssertBodylessReveal(fixture);
    }

    [Fact]
    public async Task The_signature_self_check_transmits_exactly_an_empty_object()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Body = """{"routeId":"signature-diagnostic","method":"POST","coveredComponents":["@method"],"effectiveScopes":["diagnostics:use"]}""";

        await fixture.Client.Diagnostics.CheckSignatureAsync(TestContext.Current.CancellationToken);

        var request = fixture.Stub.LastRequest!;

        // The diagnostic declares a body and requires exactly {} — the opposite of a reveal.
        Assert.Equal("{}", Encoding.UTF8.GetString(fixture.Stub.LastRequestBody!));
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(EmptyObjectDigest, request.Content.Headers.GetValues("Content-Digest").Single());
        Assert.True(request.Headers.Contains("Nonce"));
        AssertNonceIsBound(request);
    }

    // Anis binds the nonce twice: the Nonce header and the ;nonce signature parameter must be equal on a
    // nonce-bearing route, or the request is refused before its signature is checked (found live 2026-09-23).
    private static void AssertNonceIsBound(HttpRequestMessage request)
    {
        var nonce = request.Headers.GetValues("Nonce").Single();

        Assert.EndsWith($";nonce=\"{nonce}\"", request.Headers.GetValues("Signature-Input").Single(), StringComparison.Ordinal);
    }

    // The gateway refuses any byte on a route that declares no body, so a reveal sends none — and still
    // covers content-digest, over the empty string.
    private static void AssertBodylessReveal(PipelineFixture fixture)
    {
        var request = fixture.Stub.LastRequest!;

        Assert.Empty(fixture.Stub.LastRequestBody!);
        Assert.Null(request.Content!.Headers.ContentType);
        Assert.Equal(EmptyDigest, request.Content.Headers.GetValues("Content-Digest").Single());
        Assert.True(request.Headers.Contains("Nonce"));
        Assert.Contains("\"content-digest\"", request.Headers.GetValues("Signature-Input").Single(), StringComparison.Ordinal);
        AssertNonceIsBound(request);
    }

    [Fact]
    public async Task An_order_carries_the_callers_idempotency_key()
    {
        using var fixture = new PipelineFixture();

        var operationId = Guid.Parse("9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34");

        fixture.Stub.Status = HttpStatusCode.Created;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = $$"""{"operationId":"{{operationId:D}}","status":"completed"}""";

        await fixture.Client.Orders.CreateAsync(
            Wallet,
            operationId,
            new CreateOrderRequest
            {
                CardId = Guid.Parse("8d4b1e73-9a25-4c60-8f37-6b2e9d5a1c48"),
                Quantity = 2,
                ExpectedUnitPrice = new Money(10.500m, "LYD"),
                ExpectedTotal = new Money(21.000m, "LYD"),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            operationId.ToString("D"),
            fixture.Stub.LastRequest!.Headers.GetValues("Idempotency-Key").Single());
        AssertNonceIsBound(fixture.Stub.LastRequest!);

        // Scale exactly three on the wire, never a JSON number.
        Assert.Contains("\"amount\":\"21.000\"", Encoding.UTF8.GetString(fixture.Stub.LastRequestBody!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tampered_body_is_discarded_rather_than_returned()
    {
        using var fixture = new PipelineFixture();

        // The stub signs whatever it is given, so signing a DIFFERENT body than it declares is the closest
        // thing to a man in the middle: the digest no longer describes the bytes. An order read, because its
        // answers are signed; an information read's are not, and are not verified.
        fixture.Stub.Body = """{"operationId":"9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34","status":"completed"}""";

        using var tampering = new TamperingHandler(fixture.Http);

        var failure = await Assert.ThrowsAsync<UnverifiableResponseException>(
            () => fixture.Client.Orders.GetAsync(Guid.Parse("9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34"), TestContext.Current.CancellationToken));

        Assert.Equal(ResponseVerificationFailure.SignatureInvalid, failure.Failure);
    }

    [Fact]
    public void A_credential_never_reaches_a_log_through_ToString()
    {
        var credential = new RevealedCredential
        {
            SoldCardId = SoldCard,
            SerialNumber = "SN-0001",
            Voucher = "1234-5678-9012",
        };

        var rendered = credential.ToString();

        Assert.DoesNotContain("1234-5678-9012", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("SN-0001", rendered, StringComparison.Ordinal);
        Assert.Contains("redacted", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Money_multiplies_exactly_and_renders_at_scale_three()
    {
        var unit = new Money(10.500m, "LYD");

        Assert.Equal("10.500", unit.ToWireAmount());
        Assert.Equal("21.000", unit.Multiply(2).ToWireAmount());
        Assert.Equal(0.3m, new Money(0.1m, "LYD").Multiply(3).Amount);
    }

    // Rewrites one byte of the response body after it was signed.
    private sealed class TamperingHandler : IDisposable
    {
        public TamperingHandler(HttpClient http)
        {
            var field = typeof(HttpMessageInvoker).GetField("_handler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var verifying = (PartnerVerifyingHandler)field!.GetValue(http)!;
            var inner = typeof(DelegatingHandler).GetProperty("InnerHandler")!;
            var signing = (DelegatingHandler)inner.GetValue(verifying)!;

            inner.SetValue(verifying, new MitmHandler { InnerHandler = signing });
        }

        public void Dispose()
        {
        }

        private sealed class MitmHandler : DelegatingHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = await base.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                body[^2] = body[^2] == (byte)'"' ? (byte)'!' : (byte)'"';

                var digest = $"sha-256=:{Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(body))}:";
                var replaced = new ByteArrayContent(body);

                foreach (var header in response.Content.Headers)
                    replaced.Headers.TryAddWithoutValidation(header.Key, header.Value);

                replaced.Headers.Remove("Content-Digest");
                replaced.Headers.TryAddWithoutValidation("Content-Digest", digest);

                response.Content = replaced;

                return response;
            }
        }
    }
}
