using System.Net;
using System.Security.Cryptography;
using System.Text;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Tests;

/// <summary>An inner handler that answers with a correctly signed Partner response.</summary>
/// <remarks>
/// It signs using the SDK's own response base builder. That is legitimate here and only here: the base
/// builder is independently proven against 33 vectors produced by the GATEWAY's signer, so a test built on
/// it exercises the pipeline — handler order, body freezing, request binding, deserialization — rather
/// than re-testing the thing it depends on.
/// </remarks>
internal sealed class SignedResponseStub(ECDsa key, string keyId) : HttpMessageHandler
{
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public string Body { get; set; } = "{}";

    /// <summary>Consecutive bodies, when a test needs more than one round trip. Overrides <see cref="Body"/>.</summary>
    public Queue<string> Bodies { get; } = new();

    /// <summary>Every request this stub saw, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    public string? Location { get; set; }

    public int? RetryAfterSeconds { get; set; }

    public bool IdempotencyReplayed { get; set; }

    public bool NoStore { get; set; }

    public DateTimeOffset SignedAt { get; set; } = DateTimeOffset.UnixEpoch;

    /// <summary>When set, the call fails with this instead of answering — a timeout or a lost connection.</summary>
    public Func<Exception>? Failure { get; set; }

    public HttpRequestMessage? LastRequest { get; private set; }

    public byte[]? LastRequestBody { get; private set; }

    /// <summary>The body of every request, in order, read before the request is disposed.</summary>
    public List<byte[]?> RequestBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        Requests.Add(request);
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        RequestBodies.Add(LastRequestBody);

        if (Failure is { } failure)
            throw failure();

        var body = Encoding.UTF8.GetBytes(Bodies.Count > 0 ? Bodies.Dequeue() : Body);
        var digest = $"sha-256=:{Convert.ToBase64String(SHA256.HashData(body))}:";
        const string RequestId = "01J9R2K8T4V6XQ0M3B7C5D9E1F";

        var components = PartnerResponseSignatureBase.Components(
            (int)Status,
            digest,
            RequestId,
            request.Headers.TryGetValues("Signature-Input", out var sent) ? string.Join(", ", sent) : null,
            Location,
            RetryAfterSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            IdempotencyReplayed ? "true" : null,
            NoStore ? "no-store" : null);

        var created = SignedAt.ToUnixTimeSeconds();
        var signature = key.SignData(
            PartnerResponseSignatureBase.Build(components, created, keyId),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var response = new HttpResponseMessage(Status) { Content = new ByteArrayContent(body) };

        response.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        response.Content.Headers.TryAddWithoutValidation("Content-Digest", digest);
        response.Headers.TryAddWithoutValidation("X-Request-Id", RequestId);

        if (Location is { } location)
            response.Headers.TryAddWithoutValidation("Location", location);

        if (RetryAfterSeconds is { } retryAfter)
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (IdempotencyReplayed)
            response.Headers.TryAddWithoutValidation("Idempotency-Replayed", "true");

        if (NoStore)
            response.Headers.TryAddWithoutValidation("Cache-Control", "no-store");

        response.Headers.TryAddWithoutValidation(
            "Signature-Input", PartnerResponseSignatureBase.SignatureInputHeader(components, created, keyId));
        response.Headers.TryAddWithoutValidation(
            "Signature", $"sig1=:{Convert.ToBase64String(signature)}:");

        return response;
    }
}
