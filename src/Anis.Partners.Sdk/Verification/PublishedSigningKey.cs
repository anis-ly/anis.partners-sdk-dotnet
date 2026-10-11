using System.Text.Json.Serialization;

namespace Anis.Partners.Sdk.Verification;

/// <summary>A public response-signing key and its lifecycle in the published key document.</summary>
public sealed record PublishedSigningKey
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

    /// <summary>One of <c>active</c>, <c>next</c> or <c>retired</c>.</summary>
    [JsonPropertyName("status")] public string? Status { get; init; }

    /// <summary>When this key can start signing responses.</summary>
    [JsonPropertyName("notBefore")] public DateTimeOffset? NotBefore { get; init; }

    /// <summary>When a retired key stopped signing responses.</summary>
    [JsonPropertyName("retiredAt")] public DateTimeOffset? RetiredAt { get; init; }
}

/// <summary>A compromised response-signing key.</summary>
public sealed record RevokedSigningKey
{
    /// <summary>The compromised key version.</summary>
    [JsonPropertyName("kid")] public string? Kid { get; init; }

    /// <summary>The compromise instant.</summary>
    [JsonPropertyName("compromisedAt")] public DateTimeOffset? CompromisedAt { get; init; }
}
