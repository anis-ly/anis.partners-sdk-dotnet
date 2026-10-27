using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sdk.Verification;

/// <summary>Verifies notifications against a trusted endpoint URL, application, published key source and clock.</summary>
/// <remarks>
/// Supply the externally registered URL from trusted configuration, including its query. Capture raw bytes before
/// JSON middleware. After verification, durably deduplicate the envelope id before processing or acknowledging.
/// The key source must fetch only the configured Anis HTTPS authority's published signing-key document.
/// </remarks>
public sealed class PartnerWebhookVerifier
{
    private readonly ISigningKeySource _keys;
    private readonly string _configuredUrl;
    private readonly Guid _applicationId;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a receiver verifier independently of request signing or dependency injection.</summary>
    /// <param name="keys">The trusted published-key source, such as HttpSigningKeySource.</param>
    /// <param name="configuredUrl">The externally registered HTTPS URL, including its query.</param>
    /// <param name="applicationId">The application expected from trusted configuration.</param>
    /// <param name="timeProvider">The clock used for the five-minute validity window.</param>
    public PartnerWebhookVerifier(ISigningKeySource keys, Uri configuredUrl, Guid applicationId, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(configuredUrl);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (!configuredUrl.IsAbsoluteUri || configuredUrl.Scheme != Uri.UriSchemeHttps || configuredUrl.Port != 443
            || configuredUrl.UserInfo.Length != 0 || configuredUrl.Fragment.Length != 0)
            throw new ArgumentException("Configure the registered absolute HTTPS URL on port 443 without user-info or a fragment.", nameof(configuredUrl));
        if (applicationId == Guid.Empty)
            throw new ArgumentException("Configure a nonempty application id.", nameof(applicationId));
        _keys = keys;
        _configuredUrl = configuredUrl.OriginalString;
        _applicationId = applicationId;
        _timeProvider = timeProvider;
    }

    /// <summary>Verifies the signature and configured receiver bindings without parsing the envelope.</summary>
    public async ValueTask VerifyAsync(VerifiableWebhook webhook, CancellationToken cancellationToken = default)
        => _ = await VerifyCoreAsync(webhook, cancellationToken).ConfigureAwait(false);

    /// <summary>Verifies before parsing typed data. Unknown future types retain their raw type and JSON object.</summary>
    public async ValueTask<WebhookEnvelope> VerifyAndParseAsync(VerifiableWebhook webhook, CancellationToken cancellationToken = default)
    {
        var verified = await VerifyCoreAsync(webhook, cancellationToken).ConfigureAwait(false);
        return ParseEnvelope(verified.Body, verified.EventId, verified.ApplicationId);
    }

    private async ValueTask<(byte[] Body, string EventId, Guid ApplicationId)> VerifyCoreAsync(
        VerifiableWebhook webhook, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webhook);
        ArgumentNullException.ThrowIfNull(webhook.Headers);
        ArgumentNullException.ThrowIfNull(webhook.Body);
        cancellationToken.ThrowIfCancellationRequested();

        if (!HasHeader(webhook.Headers, "Signature-Input") || !HasHeader(webhook.Headers, "Signature"))
            throw Fail(WebhookVerificationFailure.SignatureMissing);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in webhook.Headers)
        {
            if (!headers.TryAdd(pair.Key, pair.Value))
                throw Fail(WebhookVerificationFailure.SignatureMalformed);
        }
        // Snapshot once: caller-owned buffers cannot change while a key refresh is awaiting the network.
        var body = webhook.Body.ToArray();
        var signatureInput = headers["Signature-Input"];
        var signature = headers["Signature"];
        if (!WebhookSignatureInputParser.TryParse(signatureInput, out var parsed)
            || signature.Contains(',') || signature.Contains('\r') || signature.Contains('\n'))
            throw Fail(WebhookVerificationFailure.SignatureMalformed);
        var equals = signature.IndexOf('=');
        if (equals <= 0 || signature.Length <= equals + 2 || signature[equals + 1] != ':' || !signature.EndsWith(':'))
            throw Fail(WebhookVerificationFailure.SignatureMalformed);
        if (parsed.Label != PartnerWebhookSignatureBase.Label
            || signature[..equals] != PartnerWebhookSignatureBase.Label)
            throw Fail(WebhookVerificationFailure.LabelUnexpected);
        if (parsed.Algorithm != PartnerWebhookSignatureBase.Algorithm)
            throw Fail(WebhookVerificationFailure.AlgorithmNotSupported);
        if (parsed.Tag != PartnerWebhookSignatureBase.Profile)
            throw Fail(WebhookVerificationFailure.ProfileUnexpected);

        var digest = Header(headers, "Content-Digest");
        var computed = $"sha-256=:{Convert.ToBase64String(SHA256.HashData(body))}:";
        if (digest is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(digest)))
            throw Fail(WebhookVerificationFailure.ContentDigestMismatch);

        var eventId = Header(headers, "webhook-id");
        var attempt = Header(headers, "anis-webhook-attempt");
        var application = Header(headers, "anis-application-id");
        if (!PartnerWebhookSignatureBase.Identifiers.SequenceEqual(parsed.Identifiers, StringComparer.Ordinal)
            || !BindingHeader(eventId) || !BindingHeader(application) || !BindingHeader(attempt)
            || attempt!.Any(character => character is < '0' or > '9')
            || !int.TryParse(attempt, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
            throw Fail(WebhookVerificationFailure.CoveredComponentsMismatch);

        if (parsed.Expires is not { } expires || expires < parsed.Created || expires - parsed.Created != 300)
            throw Fail(WebhookVerificationFailure.ValidityWindowInvalid);
        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (now > expires)
            throw Fail(WebhookVerificationFailure.SignatureExpired);
        if (Math.Abs((double)now - parsed.Created) > 300)
            throw Fail(WebhookVerificationFailure.CreatedOutOfWindow);
        if (webhook.Method != "POST")
            throw Fail(WebhookVerificationFailure.MethodUnexpected);
        if (!string.Equals(webhook.TargetUri, _configuredUrl, StringComparison.Ordinal))
            throw Fail(WebhookVerificationFailure.TargetUriMismatch);
        if (!Guid.TryParseExact(application, "D", out var applicationId) || applicationId != _applicationId)
            throw Fail(WebhookVerificationFailure.ApplicationIdMismatch);

        var document = await _keys.GetAsync(cancellationToken).ConfigureAwait(false);
        ValidateDocument(document);
        RejectCompromised(document, parsed.KeyId);
        var candidates = document.Keys.Where(key => key.Kid == parsed.KeyId).ToArray();
        if (candidates.Length == 0)
        {
            document = await _keys.RefreshAsync(cancellationToken).ConfigureAwait(false);
            ValidateDocument(document);
            RejectCompromised(document, parsed.KeyId);
            candidates = document.Keys.Where(key => key.Kid == parsed.KeyId).ToArray();
        }
        if (document.Keys.Any(key => key.D is not null) || candidates.Length > 1)
            throw Fail(WebhookVerificationFailure.KeyRejected);
        if (candidates.Length == 0)
            throw Fail(WebhookVerificationFailure.UnknownKey);
        var key = candidates[0];
        if (key.Purpose != "webhook")
            throw Fail(WebhookVerificationFailure.KeyPurposeMismatch);
        if (key.Status is "compromised" or "revoked")
            throw Fail(WebhookVerificationFailure.KeyCompromised);
        if (key.NotBefore is null || (key.Status == "retired" && key.RetiredAt is null))
            throw Fail(WebhookVerificationFailure.KeyRejected);
        if (key.Status == "retired")
            throw Fail(WebhookVerificationFailure.KeyRetired);
        if (key.Status is not ("active" or "next") || key.NotBefore is null
            || key.Kty != "EC" || key.Crv != "P-256" || key.Use is not (null or "sig") || key.Alg is not (null or "ES256"))
            throw Fail(WebhookVerificationFailure.KeyRejected);
        if (DateTimeOffset.FromUnixTimeSeconds(parsed.Created) < key.NotBefore.Value)
            throw Fail(WebhookVerificationFailure.KeyNotYetValid);

        var x = Coordinate(key.X);
        var y = Coordinate(key.Y);
        if (x is null || y is null)
            throw Fail(WebhookVerificationFailure.KeyRejected);
        ECDsa ecdsa;
        try
        {
            ecdsa = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } });
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw Fail(WebhookVerificationFailure.KeyRejected);
        }
        using (ecdsa)
        {
            var signatureBytes = SignatureBytes(signature);
            if (signatureBytes is not { Length: 64 })
                throw Fail(WebhookVerificationFailure.SignatureMalformed);
            var signatureBase = PartnerWebhookSignatureBase.Build(_configuredUrl, digest, eventId!, attempt!, application!, parsed.Created, expires, parsed.KeyId);
            if (!ecdsa.VerifyData(signatureBase, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw Fail(WebhookVerificationFailure.SignatureInvalid);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return (body, eventId!, applicationId);
    }

    private static WebhookEnvelope ParseEnvelope(byte[] body, string eventId, Guid applicationId)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateMembers(root)
                || !TryId(root, "id", out var id) || !TryId(root, "applicationId", out var envelopeApplication)
                || !root.TryGetProperty("occurredAt", out var time) || time.ValueKind != JsonValueKind.String
                || !WebhookTimestamp.TryParse(time.GetString(), out var occurredAt) || occurredAt == default
                || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(type.GetString())
                || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                throw Fail(WebhookVerificationFailure.EnvelopeInvalid);
            if (!Guid.TryParseExact(eventId, "D", out var headerId) || id != headerId)
                throw Fail(WebhookVerificationFailure.EventIdMismatch);
            if (envelopeApplication != applicationId)
                throw Fail(WebhookVerificationFailure.ApplicationIdMismatch);
            var rawType = type.GetString()!;
            var known = WebhookEventTypeConverter.Parse(rawType);
            object value = known switch
            {
                WebhookEventType.MeterChargeSucceeded or WebhookEventType.MeterChargeFailed => ProductReadParser.Meter(data),
                WebhookEventType.DirectRechargePaid or WebhookEventType.DirectRechargePaymentFailed
                    or WebhookEventType.DirectRechargeSuspended or WebhookEventType.DirectRechargeCompleted
                    or WebhookEventType.DirectRechargeRejected or WebhookEventType.DirectRechargePartiallyRefunded => ProductReadParser.DirectRecharge(data),
                WebhookEventType.EndpointVerification => VerificationData(data),
                WebhookEventType.EndpointTest => TestData(data),
                _ => data.Clone(),
            };
            return new WebhookEnvelope { Id = id, RawType = rawType, OccurredAt = occurredAt.ToUniversalTime(), ApplicationId = envelopeApplication, Data = value };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            throw Fail(WebhookVerificationFailure.EnvelopeInvalid);
        }
    }

    private static WebhookVerificationData VerificationData(JsonElement data)
    {
        var value = data.Deserialize<WebhookVerificationData>(AnisJson.Options) ?? throw new JsonException();
        if (value.Generation <= 0 || value.Challenge is null || value.Challenge.Length != 43
            || value.Challenge.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new JsonException();
        try
        {
            var bytes = Convert.FromBase64String(value.Challenge.Replace('-', '+').Replace('_', '/') + "=");
            if (bytes.Length != 32 || Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != value.Challenge)
                throw new JsonException();
        }
        catch (FormatException)
        {
            throw new JsonException();
        }
        return value;
    }

    private static WebhookTestData TestData(JsonElement data)
    {
        var value = data.Deserialize<WebhookTestData>(AnisJson.Options) ?? throw new JsonException();
        if (string.IsNullOrEmpty(value.Message))
            throw new JsonException();
        return value;
    }

    private static bool TryId(JsonElement value, string name, out Guid id)
    {
        id = default;
        return value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            && property.TryGetGuid(out id) && id != Guid.Empty;
    }

    private static bool HasDuplicateMembers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Any(HasDuplicateMembers);
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        return value.EnumerateObject().Any(property => !names.Add(property.Name) || HasDuplicateMembers(property.Value));
    }

    private static bool BindingHeader(string? value)
        => !string.IsNullOrEmpty(value) && !value.Any(character => character is '\r' or '\n' or ',');

    private static bool HasHeader(IReadOnlyDictionary<string, string> headers, string name)
        => headers.Any(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(pair.Value));

    private static string? Header(IReadOnlyDictionary<string, string> headers, string name)
        => headers.TryGetValue(name, out var value) ? value : null;

    private static void ValidateDocument(PartnerSigningKeySet document)
    {
        if (document is null || document.Keys is null || document.Revoked is null
            || document.Keys.Any(key => key is null) || document.Revoked.Any(key => key is null))
            throw Fail(WebhookVerificationFailure.KeyRejected);
    }

    private static void RejectCompromised(PartnerSigningKeySet document, string keyId)
    {
        if (document.Revoked.Any(key => key.Kid == keyId))
            throw Fail(WebhookVerificationFailure.KeyCompromised);
    }

    private static byte[]? Coordinate(string? value)
    {
        if (value is null || value.Length != 43 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            return null;
        try
        {
            var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + "=");
            return bytes.Length == 32 && Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') == value ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static byte[]? SignatureBytes(string signature)
    {
        const string prefix = "sig1=:";
        if (!signature.StartsWith(prefix, StringComparison.Ordinal) || !signature.EndsWith(':'))
            return null;
        var encoded = signature[prefix.Length..^1];
        if (encoded.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('+' or '/' or '=')))
            return null;
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            return Convert.ToBase64String(bytes) == encoded ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static UnverifiableWebhookException Fail(WebhookVerificationFailure failure) => new(failure);
}
