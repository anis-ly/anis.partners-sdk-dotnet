using System.Text.Json.Serialization;

namespace Anis.Partners.Sdk.Verification;

/// <summary>A public P-256 key for enrollment and thumbprint computation.</summary>
public sealed record PartnerJwk
{
    /// <summary>Always <c>EC</c>.</summary>
    [JsonPropertyName("kty")] public string? Kty { get; init; }

    /// <summary>Always <c>P-256</c>.</summary>
    [JsonPropertyName("crv")] public string? Crv { get; init; }

    /// <summary>Base64url X coordinate.</summary>
    [JsonPropertyName("x")] public string? X { get; init; }

    /// <summary>Base64url Y coordinate.</summary>
    [JsonPropertyName("y")] public string? Y { get; init; }

    /// <summary>The key version a response's <c>keyid</c> names: <c>keyName/keyVersion</c>.</summary>
    [JsonPropertyName("kid")] public string? Kid { get; init; }

    /// <summary>Intended use, when published.</summary>
    [JsonPropertyName("use")] public string? Use { get; init; }

    /// <summary>Algorithm, when published.</summary>
    [JsonPropertyName("alg")] public string? Alg { get; init; }

    /// <summary>
    /// A private scalar. NEVER present on a genuine document, and captured so its presence can be REFUSED.
    /// </summary>
    /// <remarks>
    /// A document carrying <c>d</c> is either compromised or is not the document it claims to be. Ignoring
    /// the member and using the public half beside it means a client keeps trusting a key set that has
    /// demonstrably leaked — so the whole document is rejected, not just that key.
    /// </remarks>
    [JsonPropertyName("d")] public string? D { get; init; }
}

/// <summary>The body of the published signing-key document.</summary>
public sealed record PartnerSigningKeySet
{
    /// <summary>Active, next and retired response-signing keys.</summary>
    [JsonPropertyName("keys")] public IReadOnlyList<PublishedSigningKey> Keys { get; init; } = [];

    /// <summary>Compromised response-signing keys, refused regardless of their signing time.</summary>
    [JsonPropertyName("revoked")] public IReadOnlyList<RevokedSigningKey> Revoked { get; init; } = [];
}
