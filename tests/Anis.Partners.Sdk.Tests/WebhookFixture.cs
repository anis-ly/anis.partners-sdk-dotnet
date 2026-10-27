using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Tests;

internal sealed class WebhookFixture : IDisposable
{
    internal const string Target = "https://pos.example.ly/hooks/anis?tenant=one%2Btwo";
    private const string KeyId = "test-webhook-key";
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly Guid Application = Guid.Parse("66666666-6666-4666-8666-666666666666");

    internal WebhookFixture()
    {
        var point = _key.ExportParameters(false).Q;
        Verifier = new PartnerWebhookVerifier(new StaticSigningKeySource(new PartnerSigningKeySet
        {
            Keys = [new PublishedSigningKey
            {
                Kid = KeyId, Purpose = "webhook", Status = "active", NotBefore = PipelineFixture.Now.AddDays(-1),
                Kty = "EC", Crv = "P-256", X = Base64Url(point.X!), Y = Base64Url(point.Y!),
            }],
        }), new Uri(Target), Application, new FixedClock());
    }

    internal PartnerWebhookVerifier Verifier { get; }

    internal VerifiableWebhook Create(string type, JsonNode data, Guid? eventId = null)
    {
        var id = eventId ?? Guid.NewGuid();
        var body = JsonSerializer.SerializeToUtf8Bytes(new { id, type, occurredAt = PipelineFixture.Now, applicationId = Application, data });
        var digest = $"sha-256=:{Convert.ToBase64String(SHA256.HashData(body))}:";
        var created = PipelineFixture.Now.ToUnixTimeSeconds();
        var parameters = "(\"@method\" \"@target-uri\" \"content-digest\" \"webhook-id\" \"anis-webhook-attempt\" \"anis-application-id\")"
            + $";created={created};expires={created + 300};keyid=\"{KeyId}\";alg=\"ecdsa-p256-sha256\";tag=\"anis-partner-webhook-v1\"";
        var signatureBase = $"\"@method\": POST\n\"@target-uri\": {Target}\n\"content-digest\": {digest}"
            + $"\n\"webhook-id\": {id:D}\n\"anis-webhook-attempt\": 1\n\"anis-application-id\": {Application:D}\n\"@signature-params\": {parameters}";
        var signature = _key.SignData(Encoding.UTF8.GetBytes(signatureBase), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new VerifiableWebhook("POST", Target, new Dictionary<string, string>
        {
            ["Signature-Input"] = "sig1=" + parameters,
            ["Signature"] = $"sig1=:{Convert.ToBase64String(signature)}:",
            ["Content-Digest"] = digest, ["webhook-id"] = id.ToString("D"),
            ["anis-webhook-attempt"] = "1", ["anis-application-id"] = Application.ToString("D"),
        }, body);
    }

    public void Dispose() => _key.Dispose();

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => PipelineFixture.Now;
    }
}
