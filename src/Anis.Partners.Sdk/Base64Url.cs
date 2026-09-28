namespace Anis.Partners.Sdk;

/// <summary>Base64url without padding (RFC 4648 §5): the encoding of nonces, key coordinates and proof signatures.</summary>
internal static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
