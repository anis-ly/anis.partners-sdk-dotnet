using System.Security.Cryptography;

namespace Anis.Partners.Sdk.Signing;

/// <summary>The RFC 9530 <c>Content-Digest</c> of a request body.</summary>
/// <remarks>
/// Taken over the EXACT bytes that go on the wire. That is not a style note: the gateway compares this
/// digest against its own hash of the bytes it received, in fixed time, BEFORE it reads any data
/// or writes evidence. A body re-serialized after the digest is computed —
/// different whitespace, a re-ordered property, a re-encoded string — fails there, and fails before
/// anything in the platform has looked at who you are.
/// </remarks>
internal static class ContentDigest
{
    /// <summary>The exact two bytes the signature diagnostic transmits: the contract's <c>EmptyBody</c>.</summary>
    /// <remarks>
    /// The two "bodyless" nonce mutations differ, and the difference is enforced by the gateway, not by
    /// the signature check. The signature diagnostic declares a request body and requires exactly <c>{}</c>. The two
    /// reveals declare none and are refused with <c>validation_failed</c> if any byte arrives, so they
    /// transmit ZERO bytes and their <c>Content-Digest</c> is the SHA-256 of the empty string. Both profiles
    /// cover <c>content-digest</c>, so each must digest exactly what it sends.
    /// </remarks>
    public static ReadOnlySpan<byte> EmptyBody => "{}"u8;

    /// <summary>Renders the structured-field value: <c>sha-256=:BASE64:</c>.</summary>
    public static string Of(ReadOnlySpan<byte> body)
        => $"sha-256=:{Convert.ToBase64String(SHA256.HashData(body))}:";
}
