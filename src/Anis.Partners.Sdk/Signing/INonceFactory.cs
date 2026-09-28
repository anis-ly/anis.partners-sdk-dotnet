using System.Security.Cryptography;

namespace Anis.Partners.Sdk.Signing;

/// <summary>Produces the single-use <c>Nonce</c> value of a nonce-bearing mutation.</summary>
/// <remarks>
/// Substitutable so the conformance corpus can pin a nonce. Nothing else should replace it: a nonce that
/// repeats is a request the gateway refuses as a replay, and a nonce that is guessable is not a nonce.
/// </remarks>
public interface INonceFactory
{
    /// <summary>A fresh value, never returned twice by this process.</summary>
    string Create();
}

/// <summary>128 bits from the cryptographic RNG, base64url. The default, and the one to use.</summary>
public sealed class RandomNonceFactory : INonceFactory
{
    /// <inheritdoc/>
    public string Create()
        => Base64Url.Encode(RandomNumberGenerator.GetBytes(16));
}
