using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Anis.Partners.Sdk.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anis.Partners.Sdk.Verification;

/// <summary>A response to check: exactly what arrived, plus what the client sent.</summary>
/// <param name="Status">The HTTP status code, which is the first covered component.</param>
/// <param name="Headers">Exactly the headers that arrived. Lookup is by the contract's spelling.</param>
/// <param name="Body">The exact bytes received. The digest is taken over these and nothing else.</param>
/// <param name="RequestSignatureInput">
/// The <c>Signature-Input</c> value THIS CLIENT SENT. The response signature covers it with <c>;req</c>,
/// and the client supplies its own copy — it never learns what the server bound, which is why a response
/// bound to a different request fails as an invalid signature rather than as a binding-specific error.
/// </param>
internal sealed record VerifiableResponse(
    int Status,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body,
    string? RequestSignatureInput);

/// <summary>Verifies a signed Partner response, or refuses it.</summary>
/// <remarks>
/// The order of the checks is load-bearing, not stylistic. In particular the digest is compared BEFORE the
/// signature: the signature covers the <c>Content-Digest</c> HEADER, not the body, so a client that checks
/// the signature first will accept a swapped body whenever the attacker also rewrote the digest.
/// </remarks>
internal sealed class PartnerResponseVerifier(
    ISigningKeySource keys,
    TimeProvider timeProvider,
    ILogger<PartnerResponseVerifier>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<PartnerResponseVerifier>.Instance;


    private const string ContentDigestHeader = "Content-Digest";
    private const string RequestIdHeader = "X-Request-Id";
    private const string SignatureInputHeader = "Signature-Input";
    private const string SignatureHeader = "Signature";

    /// <summary>Throws <see cref="UnverifiableResponseException"/> unless every rule holds.</summary>
    public async ValueTask VerifyAsync(VerifiableResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        // 1. Present at all.
        if (!TryHeader(response, SignatureInputHeader, out var signatureInput) || !TryHeader(response, SignatureHeader, out var signature))
            throw Fail(ResponseVerificationFailure.SignatureMissing, "The response carried no signature headers.");

        // 2-4. Parse, and the two frozen constants.
        if (!SignatureInputParser.TryParse(signatureInput, out var parsed))
            throw Fail(ResponseVerificationFailure.SignatureMalformed, "Signature-Input could not be parsed.");

        if (parsed.Label != PartnerResponseSignatureBase.Label
            || !signature.StartsWith(PartnerResponseSignatureBase.Label + "=", StringComparison.Ordinal))
        {
            throw Fail(ResponseVerificationFailure.LabelUnexpected, "The label is frozen at sig1 on both headers.");
        }

        if (parsed.Algorithm is not null && parsed.Algorithm != PartnerResponseSignatureBase.Algorithm)
            throw Fail(ResponseVerificationFailure.AlgorithmNotSupported, "There is no alternate algorithm to negotiate.");

        // 5. The digest describes the body that arrived. Before anything cryptographic.
        if (!TryHeader(response, ContentDigestHeader, out var contentDigest))
            throw Fail(ResponseVerificationFailure.ContentDigestMismatch, "The response carried no Content-Digest.");

        var computed = $"sha-256=:{Convert.ToBase64String(SHA256.HashData(response.Body))}:";

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(contentDigest)))
            throw Fail(ResponseVerificationFailure.ContentDigestMismatch, "The Content-Digest does not describe the body.");

        if (!TryHeader(response, RequestIdHeader, out var requestId))
            throw Fail(ResponseVerificationFailure.CoveredComponentsMismatch, "The response carried no X-Request-Id.");

        // 6. Rebuild the profile from what the response CARRIES, then compare. Never honour the advertised
        //    list: one that omits content-digest is validly signed over a body nothing protects.
        var components = PartnerResponseSignatureBase.Components(
            response.Status,
            contentDigest,
            requestId,
            response.RequestSignatureInput,
            Header(response, "Location"),
            Header(response, "Retry-After"),
            Header(response, "Idempotency-Replayed"),
            Header(response, "Cache-Control"));

        var expected = components.Select(component => component.Identifier.Replace("\"", string.Empty, StringComparison.Ordinal)).ToList();

        if (!expected.SequenceEqual(parsed.Identifiers, StringComparer.Ordinal))
        {
            throw Fail(
                ResponseVerificationFailure.CoveredComponentsMismatch,
                "The advertised components are not the ones partner-response-v1 produces for this response.");
        }

        // 7. The base names ;req but this client has no request signature to put there.
        if (parsed.Identifiers.Any(identifier => identifier.EndsWith(";req", StringComparison.Ordinal))
            && string.IsNullOrEmpty(response.RequestSignatureInput))
        {
            throw Fail(ResponseVerificationFailure.CoveredComponentsMismatch, "The request binding cannot be rebuilt.");
        }

        // 8. A response signature has created and no expires, so freshness is the client's to bound.
        var age = timeProvider.GetUtcNow().ToUnixTimeSeconds() - parsed.Created;

        if (Math.Abs(age) > PartnerResponseSignatureBase.MaxAge.TotalSeconds)
            throw Fail(ResponseVerificationFailure.CreatedOutOfWindow, $"created is {age}s from this clock.");

        // 9. Resolve from the PUBLISHED document, refreshing once for a rotation.
        var document = await keys.GetAsync(cancellationToken).ConfigureAwait(false);
        var key = Resolve(document, parsed.KeyId);

        if (key is null)
        {
            // A key version this client has not seen is the ordinary signal that a rotation happened.
            // Exactly one refresh: unbounded refreshing would turn a hostile keyid into a way to make a
            // client hammer the gateway.
            Log.UnknownSigningKey(_logger, parsed.KeyId);

            document = await keys.RefreshAsync(cancellationToken).ConfigureAwait(false);
            key = Resolve(document, parsed.KeyId);
        }

        if (document.Keys.Any(candidate => !string.IsNullOrEmpty(candidate.D)))
        {
            throw Fail(
                ResponseVerificationFailure.KeyRejected,
                "The published key document carries a private member, so the whole document is refused.");
        }

        if (key is null)
            throw Fail(ResponseVerificationFailure.UnknownKey, $"keyid {parsed.KeyId} is not a published key version.");

        // 10-11. P1363 by shape, then by verification over the REBUILT base.
        var signatureBytes = ByteSequence(signature);

        if (signatureBytes is not { Length: 64 })
        {
            throw Fail(
                ResponseVerificationFailure.SignatureMalformed,
                $"The signature is {signatureBytes?.Length.ToString(CultureInfo.InvariantCulture) ?? "not a byte sequence"}; P-256 P1363 is exactly 64 bytes.");
        }

        var signatureBase = PartnerResponseSignatureBase.Build(components, parsed.Created, parsed.KeyId);

        // RFC 7518 §6.2.1.2: a P-256 coordinate is exactly 32 bytes. A published key of any other shape is
        // refused as a key problem — never passed to the platform, which would throw its own exception past
        // every caller's handling of an unverifiable response.
        if (Coordinate(key.X) is not { } x || Coordinate(key.Y) is not { } y)
        {
            throw Fail(
                ResponseVerificationFailure.KeyRejected,
                $"The published key {parsed.KeyId} is not a P-256 key with two 32-byte coordinates.");
        }

        ECDsa ecdsa;

        try
        {
            ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = x, Y = y },
            });
        }
        catch (CryptographicException)
        {
            // Well-formed bytes that are not a point on the curve.
            throw Fail(ResponseVerificationFailure.KeyRejected, $"The published key {parsed.KeyId} is not a valid P-256 point.");
        }

        using (ecdsa)
        {
            if (!ecdsa.VerifyData(signatureBase, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw Fail(ResponseVerificationFailure.SignatureInvalid, "The signature does not verify over the rebuilt base.");
        }
    }

    private const int P256CoordinateBytes = 32;

    private static byte[]? Coordinate(string? base64Url)
    {
        if (string.IsNullOrEmpty(base64Url))
            return null;

        var padded = base64Url.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');

        var buffer = new byte[padded.Length];

        return Convert.TryFromBase64String(padded, buffer, out var written) && written == P256CoordinateBytes
            ? buffer[..written]
            : null;
    }

    private static PartnerJwk? Resolve(PartnerSigningKeySet document, string keyId)
        => document.Keys.FirstOrDefault(key
            => string.Equals(key.Kid, keyId, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(key.X)
            && !string.IsNullOrEmpty(key.Y));

    private static bool TryHeader(VerifiableResponse response, string name, out string value)
    {
        value = Header(response, name) ?? string.Empty;

        return value.Length > 0;
    }

    private static string? Header(VerifiableResponse response, string name)
        => response.Headers.TryGetValue(name, out var value) ? value : null;

    private static byte[]? ByteSequence(string value)
    {
        var start = value.IndexOf(':');
        var end = value.LastIndexOf(':');

        if (start < 0 || end <= start)
            return null;

        return Convert.TryFromBase64String(value[(start + 1)..end], new byte[256], out var written)
            ? Convert.FromBase64String(value[(start + 1)..end])[..written]
            : null;
    }

    // Every refusal goes through here, so the counter and the log cannot drift apart from the throw.
    // The detail describes the RULE and never the response: no body, no base, no signature, no key.
    private UnverifiableResponseException Fail(ResponseVerificationFailure failure, string detail)
    {
        AnisPartnersTelemetry.VerificationFailures.Add(
            1,
            new KeyValuePair<string, object?>(AnisPartnersTelemetry.Tags.VerificationFailure, failure.ToString()));

        Log.ResponseDiscarded(_logger, failure.ToString());

        return new UnverifiableResponseException(failure, detail);
    }
}
