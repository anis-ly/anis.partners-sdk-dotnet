using System.Security.Cryptography;
using System.Text;

namespace Anis.Partners.Sdk.Enrollment;

/// <summary>The short code your key's fingerprint is read out as: what you say to Anis staff on the phone.</summary>
/// <remarks>
/// After the proof, Anis staff phone your technical contact and ask for this code before the key goes live. It is
/// the first 80 bits of the key's thumbprint in Crockford base32, so a code that does not match means the key Anis
/// holds is not the one you are looking at.
/// </remarks>
public static class SafetyCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int ThumbprintBytes = 32;
    private const int CodeBytes = 10;
    private const int CodeLength = 16;

    /// <summary>The safety code of a thumbprint, as <c>XXXX-XXXX-XXXX-XXXX</c>.</summary>
    /// <param name="thumbprint">The 43-character base64url thumbprint from <see cref="KeyThumbprint.Compute"/>.</param>
    /// <exception cref="ArgumentException">The value is not a base64url SHA-256 thumbprint.</exception>
    public static string FromThumbprint(string thumbprint)
    {
        if (!Base64Url.TryDecode(thumbprint, out var digest) || digest.Length != ThumbprintBytes)
            throw new ArgumentException("A thumbprint is the base64url encoding of 32 bytes (43 characters).", nameof(thumbprint));

        var code = new StringBuilder(CodeLength + 3);
        var buffer = 0;
        var bits = 0;

        foreach (var value in digest.AsSpan(0, CodeBytes))
        {
            buffer = (buffer << 8) | value;
            bits += 8;

            while (bits >= 5)
            {
                bits -= 5;
                code.Append(Alphabet[(buffer >> bits) & 31]);
            }

            buffer &= (1 << bits) - 1;
        }

        for (var at = 4; at < CodeLength + 3; at += 5)
            code.Insert(at, '-');

        return code.ToString();
    }

    /// <summary>Whether what someone entered or read out means the key with this thumbprint.</summary>
    /// <remarks>
    /// Trim, remove spaces and <c>-</c>, upper-case, read <c>O</c> as <c>0</c> and <c>I</c>, <c>L</c> as <c>1</c>.
    /// Sixteen characters are compared with the code; any other length is compared, trimmed but otherwise exactly as
    /// typed, with the full thumbprint. Both comparisons run in fixed time.
    /// </remarks>
    internal static bool Matches(string? entered, string thumbprint)
    {
        var raw = (entered ?? string.Empty).Trim();

        var candidate = raw.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant()
            .Replace('O', '0')
            .Replace('I', '1')
            .Replace('L', '1');

        return candidate.Length == CodeLength
            ? FixedTimeEquals(candidate, FromThumbprint(thumbprint).Replace("-", string.Empty, StringComparison.Ordinal))
            : FixedTimeEquals(raw, thumbprint);
    }

    internal static bool FixedTimeEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
