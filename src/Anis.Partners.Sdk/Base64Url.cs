namespace Anis.Partners.Sdk;

/// <summary>Base64url without padding (RFC 4648 §5): the encoding of nonces, key coordinates, thumbprints and proof signatures.</summary>
internal static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Decodes strict unpadded base64url; false for any other character, padding, or an impossible length.</summary>
    public static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = [];

        if (string.IsNullOrEmpty(value) || value.Length % 4 == 1)
            return false;

        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                return false;
        }

        var standard = value.Replace('-', '+').Replace('_', '/');
        standard = standard.PadRight(standard.Length + ((4 - (standard.Length % 4)) % 4), '=');

        try
        {
            bytes = Convert.FromBase64String(standard);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
