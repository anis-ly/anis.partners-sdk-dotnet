using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using Anis.Partners.Sdk.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anis.Partners.Sdk.Signing;

/// <summary>Signs every outgoing Partner request, as the last thing that touches it before the wire.</summary>
/// <remarks>
/// A <see cref="DelegatingHandler"/> is the correct seam and the only correct one. The digest must cover
/// the bytes that are actually transmitted, so signing has to happen AFTER serialization is final — which
/// rules out an "auth provider" hook that sets one header, and rules out signing in the operations layer
/// where the content has not been rendered yet.
///
/// The body is materialized once and the content is REPLACED with those exact bytes. Without that,
/// <see cref="HttpClient"/> is free to render the content a second time on its way out, and a second
/// rendering that differs by one byte fails the digest comparison at the gateway — before any data is read,
/// any evidence is written or anything could explain why.
///
/// Every signed header is SET, never appended. A host's retry handler sits outside this one and sends the
/// same request object through it again; each pass must leave with one fresh signature over its own nonce,
/// not the previous attempt's headers with a second set stacked beside them.
/// </remarks>
internal sealed class PartnerSigningHandler(
    PartnerRequestSigner signer,
    TimeProvider timeProvider,
    INonceFactory nonces,
    TimeSpan signatureLifetime,
    ILogger<PartnerSigningHandler>? logger = null) : DelegatingHandler
{
    private readonly ILogger _logger = logger ?? NullLogger<PartnerSigningHandler>.Instance;


    private const string AnisDateHeader = "X-Anis-Date";
    private const string NonceHeader = "Nonce";
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string SignatureInputHeader = "Signature-Input";
    private const string SignatureHeader = "Signature";
    private const string ContentDigestHeader = "Content-Digest";

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A request with no profile is not a Partner route; the enrollment and public routes go out
        // unsigned and are handled by their own clients.
        if (!request.Options.TryGetValue(PartnerRequestOptions.Profile, out var profile))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var uri = request.RequestUri
            ?? throw new InvalidOperationException("A Partner request must have an absolute URI before it is signed.");

        var body = await FreezeBodyAsync(request, profile, cancellationToken).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        var created = now.ToUnixTimeSeconds();
        var expires = now.Add(signatureLifetime).ToUnixTimeSeconds();

        var inputs = new SignatureInputs
        {
            Method = request.Method.Method.ToUpperInvariant(),

            // Anis lower-cases the authority it rebuilds. Uri.Authority already omits a default port.
            Authority = uri.Authority.ToLowerInvariant(),
            Path = PathOf(uri),

            // Verbatim, minus the leading '?'. Re-ordering or re-encoding here would sign a query the
            // gateway never saw.
            CanonicalQuery = uri.Query.Length > 0 ? uri.Query[1..] : string.Empty,

            AnisDate = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ContentDigest = body is null ? null : ContentDigest.Of(body),
            Nonce = profile is SignatureProfile.SafeRead ? null : nonces.Create(),
            IdempotencyKey = profile is SignatureProfile.OrderMutation
                ? request.Options.TryGetValue(PartnerRequestOptions.IdempotencyKey, out var key)
                    ? key
                    : throw new InvalidOperationException(
                        "An order mutation must carry a caller-supplied Idempotency-Key. The SDK will not "
                        + "generate one: a generated key turns a lost response plus a retry into a second "
                        + "purchase.")
                : null,
        };

        var started = Stopwatch.GetTimestamp();
        SignedRequestHeaders signed;

        try
        {
            signed = await signer.SignAsync(profile, inputs, created, expires, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Nothing has left this process: the request was never sent, so an order under it was not placed.
            // Named as its own failure so a vault or HSM outage is not mistaken for an Anis outage.
            throw new RequestSigningException(exception);
        }

        // Measured separately from the request: a vault-backed signer adds a round trip here, and inside a
        // total duration that cost is invisible.
        AnisPartnersTelemetry.SignatureDuration.Record(
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            new KeyValuePair<string, object?>(AnisPartnersTelemetry.Tags.SignatureProfile, profile.ToString()));

        Apply(request, signed);

        // The key id is public. The signature, its base and the nonce are not, and none of them is here.
        Log.RequestSigned(_logger, inputs.Method, inputs.Path, profile.ToString(), signer.KeyId);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    // Every profile other than SafeRead covers content-digest, so the bytes are frozen here and digested.
    // The OPERATION decides what those bytes are, never the profile: a reveal transmits zero bytes (the
    // gateway refuses any body on a route that does not declare one), the signature diagnostic transmits
    // exactly {} and an order transmits its JSON. A handler that rewrote an empty body into {} would turn
    // every reveal into a 422.
    private static async Task<byte[]?> FreezeBodyAsync(
        HttpRequestMessage request,
        SignatureProfile profile,
        CancellationToken cancellationToken)
    {
        if (profile is SignatureProfile.SafeRead)
            return null;

        var body = request.Content is null
            ? []
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        var frozen = new ByteArrayContent(body);

        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                // Content-Length is restated by ByteArrayContent itself from the frozen bytes, and a digest
                // left by an earlier attempt is replaced by this attempt's own.
                if (!string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(header.Key, ContentDigestHeader, StringComparison.OrdinalIgnoreCase))
                {
                    frozen.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        // A type only describes bytes that exist; an empty body carries none.
        if (body.Length > 0 && frozen.Headers.ContentType is null)
            frozen.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = null };

        request.Content = frozen;

        return body;
    }

    // Anis verifies over the DECODED path, so a percent-encoded segment would be signed one way and
    // rebuilt another. Every path on the closed route set is UUIDs and literals, so rather than guess at a
    // normalization the gateway does not perform, this refuses the case outright and says why.
    private static string PathOf(Uri uri)
    {
        var path = uri.AbsolutePath;

        if (path.Contains('%', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The request path contains a percent-encoded character. The gateway rebuilds the signature "
                + "base from its DECODED path, so such a request cannot verify. Every Anis Partner route "
                + "takes UUID path segments; a percent sign here means a value reached the path that does "
                + "not belong in one.");
        }

        return path;
    }

    private static void Apply(HttpRequestMessage request, SignedRequestHeaders signed)
    {
        Set(request.Headers, AnisDateHeader, signed.AnisDate);
        Set(request.Headers, NonceHeader, signed.Nonce);
        Set(request.Headers, IdempotencyKeyHeader, signed.IdempotencyKey?.ToString("D", CultureInfo.InvariantCulture));

        if (request.Content is not null)
            Set(request.Content.Headers, ContentDigestHeader, signed.ContentDigest);

        Set(request.Headers, SignatureInputHeader, signed.SignatureInput);
        Set(request.Headers, SignatureHeader, signed.Signature);
    }

    // TryAddWithoutValidation appends to a header that already exists, so a resent request would carry the
    // previous attempt's value beside this one. Remove first; a null value leaves the header absent.
    private static void Set(HttpHeaders headers, string name, string? value)
    {
        headers.Remove(name);

        if (value is not null)
            headers.TryAddWithoutValidation(name, value);
    }
}
