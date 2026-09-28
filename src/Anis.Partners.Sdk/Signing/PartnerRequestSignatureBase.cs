using System.Globalization;
using System.Text;

namespace Anis.Partners.Sdk.Signing;

/// <summary>Builds the RFC 9421 <c>Signature-Input</c> value and the exact signature base of a request.</summary>
/// <remarks>
/// The two are produced together, from one component list, for the same reason the gateway does it that
/// way: splitting them is how a request ends up signed over components its <c>Signature-Input</c> does not
/// name, and that failure is invisible to inspection — the headers look right and the signature is real.
/// </remarks>
internal static class PartnerRequestSignatureBase
{
    /// <summary>The only label the contract accepts, on both headers.</summary>
    public const string Label = "sig1";

    /// <summary>The only algorithm. There is no negotiation and no fallback.</summary>
    public const string Algorithm = "ecdsa-p256-sha256";

    /// <summary>Maximum <c>expires - created</c> the contract allows, in seconds.</summary>
    public const int MaxSignatureLifetimeSeconds = 300;

    /// <summary>Renders the <c>@signature-params</c> value: the identifier list and its parameters.</summary>
    /// <param name="components">The covered components, in the profile's order.</param>
    /// <param name="created">Unix seconds.</param>
    /// <param name="expires">Unix seconds, at most 300 after <paramref name="created"/>.</param>
    /// <param name="keyId">The credential.</param>
    /// <param name="nonce">
    /// The request's <c>Nonce</c> header value on a nonce-bearing profile, and null on a read. Anis
    /// binds the two: on a route that requires a nonce the <c>;nonce</c> parameter must be present and equal to
    /// the header, and on a read it must be absent — otherwise the request is refused as
    /// <c>invalid_credentials</c> before its signature is even checked.
    /// </param>
    public static string Parameters(
        IReadOnlyList<string> components,
        long created,
        long expires,
        Guid keyId,
        string? nonce = null)
    {
        ArgumentNullException.ThrowIfNull(components);

        if (nonce is not null && (nonce.Length == 0 || nonce.Any(character => character is '"' or '\\' || char.IsControl(character))))
            throw new ArgumentException("A nonce must be a non-empty structured-field string without quotes, backslashes or control characters.", nameof(nonce));

        if (expires - created > MaxSignatureLifetimeSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expires),
                expires - created,
                $"A Partner signature may live at most {MaxSignatureLifetimeSeconds} seconds. A longer window "
                + "is refused at the gateway, so it is refused here where the cause is still visible.");
        }

        var identifiers = string.Join(' ', components.Select(component => $"\"{component}\""));

        return $"({identifiers})"
            + $";created={created.ToString(CultureInfo.InvariantCulture)}"
            + $";expires={expires.ToString(CultureInfo.InvariantCulture)}"
            + $";keyid=\"{keyId.ToString("D", CultureInfo.InvariantCulture)}\""
            + $";alg=\"{Algorithm}\""
            + (nonce is null ? string.Empty : $";nonce=\"{nonce}\"");
    }

    /// <summary>The full <c>Signature-Input</c> header value.</summary>
    public static string SignatureInputHeader(string parameters) => $"{Label}={parameters}";

    /// <summary>The full <c>Signature</c> header value for a 64-byte P1363 signature.</summary>
    public static string SignatureHeader(ReadOnlySpan<byte> signature)
        => $"{Label}=:{Convert.ToBase64String(signature)}:";

    /// <summary>Builds the exact bytes that are signed.</summary>
    /// <remarks>
    /// Each covered component is one line, <c>"name": value</c>, newline-terminated. The final
    /// <c>@signature-params</c> line carries NO trailing newline — appending one changes the signed bytes
    /// and every signature fails.
    /// </remarks>
    public static byte[] Build(IReadOnlyList<string> components, SignatureInputs inputs, string parameters)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(inputs);

        var builder = new StringBuilder();

        foreach (var component in components)
            builder.Append('"').Append(component).Append("\": ").Append(inputs.ValueOf(component)).Append('\n');

        builder.Append('"').Append("@signature-params").Append("\": ").Append(parameters);

        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
