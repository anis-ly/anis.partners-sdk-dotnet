using System.Security.Cryptography;
using System.Text;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Enrollment;

/// <summary>The RFC 7638 thumbprint of a P-256 public key: the fingerprint Anis computes for the key you submit.</summary>
/// <remarks>
/// <see cref="AnisEnrollmentClient.SubmitKeyAsync"/> computes it locally and compares it with the one Anis
/// answers with, so a key that was altered on its way is caught before you prove possession of it.
/// </remarks>
public static class KeyThumbprint
{
    private const int CoordinateBytes = 32;

    /// <summary>Computes the thumbprint of a public key.</summary>
    /// <param name="publicJwk">A complete P-256 public JWK: <c>kty</c> <c>EC</c>, <c>crv</c> <c>P-256</c>, and both coordinates.</param>
    /// <returns>
    /// The base64url (no padding, 43 characters) SHA-256 of <c>{"crv":"P-256","kty":"EC","x":"…","y":"…"}</c> — the
    /// required members only, in that order, with no whitespace (RFC 7638 §3).
    /// </returns>
    /// <exception cref="ArgumentException">The JWK is not a complete P-256 public key.</exception>
    public static string Compute(PartnerJwk publicJwk)
    {
        ArgumentNullException.ThrowIfNull(publicJwk);

        if (publicJwk.Kty != "EC" || publicJwk.Crv != "P-256")
            throw new ArgumentException("The key must be an EC P-256 public JWK.", nameof(publicJwk));

        var x = Coordinate(publicJwk.X, "x");
        var y = Coordinate(publicJwk.Y, "y");

        var canonical = $$"""{"crv":"P-256","kty":"EC","x":"{{x}}","y":"{{y}}"}""";

        return Base64Url.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        static string Coordinate(string? value, string member)
            => Base64Url.TryDecode(value, out var bytes) && bytes.Length == CoordinateBytes
                ? value!
                : throw new ArgumentException($"The JWK member \"{member}\" is not a base64url P-256 coordinate (32 bytes).", nameof(publicJwk));
    }
}
