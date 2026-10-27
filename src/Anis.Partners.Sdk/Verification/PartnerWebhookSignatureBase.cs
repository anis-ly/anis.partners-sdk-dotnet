using System.Globalization;
using System.Text;

namespace Anis.Partners.Sdk.Verification;

internal static class PartnerWebhookSignatureBase
{
    internal const string Label = "sig1";
    internal const string Algorithm = "ecdsa-p256-sha256";
    internal const string Profile = "anis-partner-webhook-v1";
    internal static readonly IReadOnlyList<string> Identifiers =
        ["@method", "@target-uri", "content-digest", "webhook-id", "anis-webhook-attempt", "anis-application-id"];

    internal static byte[] Build(string targetUri, string digest, string eventId, string attempt, string applicationId,
        long created, long expires, string keyId)
    {
        var covered = string.Join(' ', Identifiers.Select(name => $"\"{name}\""));
        var parameters = $"({covered});created={created.ToString(CultureInfo.InvariantCulture)}"
            + $";expires={expires.ToString(CultureInfo.InvariantCulture)};keyid=\"{keyId}\";alg=\"{Algorithm}\";tag=\"{Profile}\"";
        return Encoding.UTF8.GetBytes($"\"@method\": POST\n\"@target-uri\": {targetUri}\n\"content-digest\": {digest}"
            + $"\n\"webhook-id\": {eventId}\n\"anis-webhook-attempt\": {attempt}\n\"anis-application-id\": {applicationId}"
            + $"\n\"@signature-params\": {parameters}");
    }
}
