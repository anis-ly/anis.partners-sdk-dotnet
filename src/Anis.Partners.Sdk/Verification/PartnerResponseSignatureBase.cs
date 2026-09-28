using System.Globalization;
using System.Text;

namespace Anis.Partners.Sdk.Verification;

/// <summary>Rebuilds the covered-component list and signature base of a signed Partner response.</summary>
/// <remarks>
/// The client REBUILDS this list from the frozen profile and the headers the response actually carries. It
/// does not honour whatever list <c>Signature-Input</c> advertises.
///
/// That distinction is the whole security of response verification. Verifying over the advertised list is
/// cryptographically sound and still wrong: a response validly signed over a list with
/// <c>content-digest</c> removed has a body that nothing protects, and a client that follows the
/// advertisement accepts it. Rebuilding means the only responses that verify are the ones the gateway's own
/// profile produces.
/// </remarks>
internal static class PartnerResponseSignatureBase
{
    /// <summary>The only label, on both headers.</summary>
    public const string Label = "sig1";

    /// <summary>The only algorithm.</summary>
    public const string Algorithm = "ecdsa-p256-sha256";

    /// <summary>The frozen profile name the gateway records on response evidence.</summary>
    public const string Profile = "partner-response-v1";

    /// <summary>
    /// How far a response's <c>created</c> may sit from the client's clock.
    /// </summary>
    /// <remarks>
    /// A response signature carries <c>created</c> and NO <c>expires</c>. Without a bound of our own a
    /// captured signed response stays replayable to a client forever, so this SDK bounds it at 60 seconds
    /// either side of its own clock. It is not the request side's rule: Anis accepts a request's
    /// <c>created</c> up to 30 seconds ahead and 300 seconds behind.
    /// </remarks>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The components this response must be signed over, in the frozen order, derived from what it carries.
    /// </summary>
    public static IReadOnlyList<PartnerResponseComponent> Components(
        int status,
        string contentDigest,
        string requestId,
        string? requestSignatureInput,
        string? location,
        string? retryAfter,
        string? idempotencyReplayed,
        string? cacheControl)
    {
        var components = new List<PartnerResponseComponent>
        {
            new("@status", status.ToString(CultureInfo.InvariantCulture), RequestBound: false),
            new("content-digest", contentDigest, RequestBound: false),
            new("x-request-id", requestId, RequestBound: false),
        };

        // Omitted rather than covered as an empty string on the enrollment branch, which carries no
        // request signature at all. A base naming a component with no value verifies against nothing.
        if (!string.IsNullOrEmpty(requestSignatureInput))
            components.Add(new("signature-input", requestSignatureInput, RequestBound: true));

        Add(components, "location", location);
        Add(components, "retry-after", retryAfter);
        Add(components, "idempotency-replayed", idempotencyReplayed);
        Add(components, "cache-control", cacheControl);

        return components;
    }

    /// <summary>Renders the <c>@signature-params</c> value. A response signature has no <c>expires</c>.</summary>
    public static string Parameters(IReadOnlyList<PartnerResponseComponent> components, long created, string keyId)
    {
        ArgumentNullException.ThrowIfNull(components);

        return $"({string.Join(' ', components.Select(component => component.Identifier))})"
            + $";created={created.ToString(CultureInfo.InvariantCulture)}"
            + $";keyid=\"{keyId}\""
            + $";alg=\"{Algorithm}\"";
    }

    /// <summary>The full <c>Signature-Input</c> header value this response should carry.</summary>
    public static string SignatureInputHeader(IReadOnlyList<PartnerResponseComponent> components, long created, string keyId)
        => $"{Label}={Parameters(components, created, keyId)}";

    /// <summary>The exact bytes that were signed; the last line carries no trailing newline.</summary>
    public static byte[] Build(IReadOnlyList<PartnerResponseComponent> components, long created, string keyId)
    {
        ArgumentNullException.ThrowIfNull(components);

        var builder = new StringBuilder();

        foreach (var component in components)
            builder.Append(component.Identifier).Append(": ").Append(component.Value).Append('\n');

        builder.Append('"').Append("@signature-params").Append("\": ").Append(Parameters(components, created, keyId));

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void Add(List<PartnerResponseComponent> components, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            components.Add(new(name, value, RequestBound: false));
    }
}

/// <summary>One covered component of a response signature.</summary>
/// <param name="Name">The lower-cased component identifier, without quotes.</param>
/// <param name="Value">The value that line carries.</param>
/// <param name="RequestBound">
/// Selects the RFC 9421 <c>;req</c> parameter, which is what binds a response signature to the request that
/// produced it. It belongs to the component rather than being a rule applied by name, because the
/// identifier appears twice — in the base line and inside <c>@signature-params</c> — and the two must agree
/// exactly.
/// </param>
internal sealed record PartnerResponseComponent(string Name, string Value, bool RequestBound)
{
    /// <summary>The quoted identifier, with <c>;req</c> outside the quotes when request-bound.</summary>
    public string Identifier => RequestBound ? $"\"{Name}\";req" : $"\"{Name}\"";
}
