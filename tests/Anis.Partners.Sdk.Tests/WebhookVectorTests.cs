using System.Text;
using System.Text.Json;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Tests;

/// <summary>The gate for webhook verification: every vector must reach its declared outcome and phase.</summary>
/// <remarks>
/// The corpus is vendored byte-for-byte from the conformance repository. Bodies are always loaded from
/// <c>bodyBase64</c>, never from the readable aid, so whitespace and escapes reach the verifier exactly as sent.
/// A rejection for the wrong reason fails the same as a wrong acceptance.
/// </remarks>
public sealed class WebhookVectorTests
{
    private static readonly Dictionary<string, WebhookVerificationFailure> Reasons = new(StringComparer.Ordinal)
    {
        ["signature_missing"] = WebhookVerificationFailure.SignatureMissing,
        ["signature_malformed"] = WebhookVerificationFailure.SignatureMalformed,
        ["signature_invalid"] = WebhookVerificationFailure.SignatureInvalid,
        ["content_digest_mismatch"] = WebhookVerificationFailure.ContentDigestMismatch,
        ["covered_components_mismatch"] = WebhookVerificationFailure.CoveredComponentsMismatch,
        ["unknown_key"] = WebhookVerificationFailure.UnknownKey,
        ["key_rejected"] = WebhookVerificationFailure.KeyRejected,
        ["algorithm_not_supported"] = WebhookVerificationFailure.AlgorithmNotSupported,
        ["label_unexpected"] = WebhookVerificationFailure.LabelUnexpected,
        ["created_out_of_window"] = WebhookVerificationFailure.CreatedOutOfWindow,
        ["profile_unexpected"] = WebhookVerificationFailure.ProfileUnexpected,
        ["key_compromised"] = WebhookVerificationFailure.KeyCompromised,
        ["key_retired"] = WebhookVerificationFailure.KeyRetired,
        ["key_not_yet_valid"] = WebhookVerificationFailure.KeyNotYetValid,
        ["signature_expired"] = WebhookVerificationFailure.SignatureExpired,
        ["validity_window_invalid"] = WebhookVerificationFailure.ValidityWindowInvalid,
        ["target_uri_mismatch"] = WebhookVerificationFailure.TargetUriMismatch,
        ["application_id_mismatch"] = WebhookVerificationFailure.ApplicationIdMismatch,
        ["key_purpose_mismatch"] = WebhookVerificationFailure.KeyPurposeMismatch,
        ["method_unexpected"] = WebhookVerificationFailure.MethodUnexpected,
        ["event_id_mismatch"] = WebhookVerificationFailure.EventIdMismatch,
        ["envelope_invalid"] = WebhookVerificationFailure.EnvelopeInvalid,
    };

    public static TheoryData<string> Vectors
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var file in VectorFiles.List("webhook", "WH-*.json"))
                data.Add(file);

            return data;
        }
    }

    [Fact]
    public void Every_failure_has_a_conformance_reason()
        => Assert.Equal(Enum.GetValues<WebhookVerificationFailure>().Order(), Reasons.Values.Order());

    [Fact]
    public void The_corpus_counts_match_its_manifest()
    {
        var files = VectorFiles.List("webhook", "WH-*.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vectors", "webhook", "manifest.json")));
        var outcomes = files.Select(file =>
        {
            using var vector = JsonDocument.Parse(File.ReadAllText(file));
            return vector.RootElement.GetProperty("expected").GetProperty("outcome").GetString();
        }).ToArray();

        Assert.Equal(manifest.RootElement.GetProperty("accept").GetInt32(), outcomes.Count(outcome => outcome == "accept"));
        Assert.Equal(manifest.RootElement.GetProperty("reject").GetInt32(), outcomes.Count(outcome => outcome == "reject"));
        Assert.Equal(manifest.RootElement.GetProperty("total").GetInt32(), outcomes.Length);
        Assert.Equal(manifest.RootElement.GetProperty("vectors").EnumerateArray()
                .Select(vector => vector.GetProperty("id").GetString()).Order(StringComparer.Ordinal),
            files.Select(file => Path.GetFileNameWithoutExtension(file)).Order(StringComparer.Ordinal));
        Assert.NotEmpty(outcomes);
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public async Task The_webhook_verifier_reaches_the_declared_outcome(string path)
    {
        var token = TestContext.Current.CancellationToken;
        var vector = JsonDocument.Parse(await File.ReadAllTextAsync(path, token)).RootElement;
        var expected = vector.GetProperty("expected");
        var request = vector.GetProperty("request");
        var receiver = vector.GetProperty("receiver");

        // Duplicate spellings are distinct keys in the corpus, so the dictionary must not fold case.
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var header in request.GetProperty("headers").EnumerateObject())
            headers[header.Name] = header.Value.GetString()!;

        var body = Convert.FromBase64String(request.GetProperty("bodyBase64").GetString()!);
        var subject = new VerifiableWebhook(request.GetProperty("method").GetString()!, request.GetProperty("targetUri").GetString()!, headers, body);

        var verifier = new PartnerWebhookVerifier(
            new StaticSigningKeySource(vector.GetProperty("signingKeys").Deserialize<PartnerSigningKeySet>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!),
            new Uri(receiver.GetProperty("configuredUrl").GetString()!),
            Guid.Parse(receiver.GetProperty("applicationId").GetString()!),
            new FixedClock(DateTimeOffset.FromUnixTimeSeconds(vector.GetProperty("verifyAt").GetInt64())));

        var phase = expected.TryGetProperty("phase", out var declared) ? declared.GetString() : "verify";

        if (expected.GetProperty("outcome").GetString() == "accept")
        {
            var envelope = await verifier.VerifyAndParseAsync(subject, token);
            var wanted = expected.GetProperty("envelope");

            Assert.Equal(Guid.Parse(wanted.GetProperty("id").GetString()!), envelope.Id);
            Assert.Equal(wanted.GetProperty("type").GetString(), envelope.RawType);
            Assert.Equal(Guid.Parse(wanted.GetProperty("applicationId").GetString()!), envelope.ApplicationId);
            Assert.NotNull(envelope.Data);

            // The data value is never re-serialized: the received bytes still hold it verbatim.
            Assert.Contains(wanted.GetProperty("dataRaw").GetString()!, Encoding.UTF8.GetString(body), StringComparison.Ordinal);

            return;
        }

        var reason = Reasons[expected.GetProperty("reason").GetString()!];

        if (phase == "parse")
        {
            // A parse-phase negative is cryptographically valid, so verification alone must succeed.
            await verifier.VerifyAsync(subject, token);
        }

        var failure = await Assert.ThrowsAsync<UnverifiableWebhookException>(
            async () => await verifier.VerifyAndParseAsync(subject, token));

        Assert.Equal(reason, failure.Failure);

        if (phase != "parse")
        {
            var verifyOnly = await Assert.ThrowsAsync<UnverifiableWebhookException>(
                async () => await verifier.VerifyAsync(subject, token));

            Assert.Equal(reason, verifyOnly.Failure);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
