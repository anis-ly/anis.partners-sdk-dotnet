namespace Anis.Partners.Sample;

/// <summary>The sample's own settings, beside the SDK's <c>AnisPartners</c> section.</summary>
/// <remarks>
/// Everything here is the partner's side of an integration: where THEIR private key file is, which key id
/// Anis issued for it, and where they persist order intent. None of it is SDK configuration, and the private
/// key itself is never a setting — only the path to a protected file.
/// </remarks>
internal sealed class SampleSettings
{
    public const string SectionName = "Sample";

    /// <summary>The PKCS#8 PEM file holding the partner's private key. Written by <c>enrol</c>.</summary>
    public string KeyFile { get; set; } = "partner-key.pem";

    /// <summary>The key id Anis issued at enrollment; the request <c>keyid</c>.</summary>
    public Guid? KeyId { get; set; }

    /// <summary>Where order intent is persisted BEFORE each order is sent, one file per operation id.</summary>
    public string OrdersFolder { get; set; } = "orders";

    /// <summary>
    /// TEST-ONLY. An <c>X-Forwarded-For</c> value this sample adds to every request, for a local stack whose
    /// gateway trusts the loopback proxy. Never set it against a real Anis deployment — its gateway ignores
    /// forwarding headers from untrusted connections — and never put it in the SDK.
    /// </summary>
    public string? ForwardedFor { get; set; }
}
