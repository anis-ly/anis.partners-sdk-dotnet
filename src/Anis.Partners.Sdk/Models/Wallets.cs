using System.Text.Json.Serialization;

namespace Anis.Partners.Sdk.Models;

/// <summary>A wallet this application may act on.</summary>
/// <remarks>
/// Access supplies the granted wallet ids and Cards intersects them with live owner and Business
/// subscription eligibility, so a wallet appearing here has passed both. Subscription identifiers,
/// enabled/expiry projections, capabilities and allowed debt are deliberately absent from the surface.
/// </remarks>
public sealed record Wallet
{
    /// <summary>Identifier.</summary>
    [JsonPropertyName("id")] public Guid Id { get; init; }

    /// <summary>Display name.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>Currency code.</summary>
    [JsonPropertyName("currency")] public string? Currency { get; init; }

    /// <summary>Reserved-adjusted balance, with the instant it was computed.</summary>
    [JsonPropertyName("balance")] public Money Balance { get; init; }
}

/// <summary>The application's own identity and the scopes its current policy grants.</summary>
/// <remarks>
/// Identifiers only. There is no Partner name, no Application name and no environment to
/// read: every deployment is a fully isolated copy, so an environment field would be a field with one
/// possible value that invited callers to branch on it.
/// </remarks>
public sealed record PartnerProfile
{
    /// <summary>The Partner this application belongs to.</summary>
    [JsonPropertyName("partner")] public PartnerIdentity? Partner { get; init; }

    /// <summary>This application and its effective scopes.</summary>
    [JsonPropertyName("application")] public ApplicationIdentity? Application { get; init; }

    /// <summary>The owner account, when the surface exposes one.</summary>
    [JsonPropertyName("ownerAccount")] public OwnerAccount? OwnerAccount { get; init; }

    /// <summary>The documentation version this deployment is aligned with.</summary>
    [JsonPropertyName("documentationVersion")] public string? DocumentationVersion { get; init; }
}

/// <summary>A Partner, by id.</summary>
public sealed record PartnerIdentity
{
    /// <summary>Identifier.</summary>
    [JsonPropertyName("id")] public Guid Id { get; init; }
}

/// <summary>An application, by id, and the scopes its current policy grants.</summary>
public sealed record ApplicationIdentity
{
    /// <summary>Identifier. This, not the key, is the principal — keys rotate beneath it.</summary>
    [JsonPropertyName("id")] public Guid Id { get; init; }

    /// <summary>The effective scopes. Re-read rather than cached: policy changes take effect per call.</summary>
    [JsonPropertyName("scopes")] public IReadOnlyList<string> Scopes { get; init; } = [];
}

/// <summary>The owner account behind the wallets.</summary>
public sealed record OwnerAccount
{
    /// <summary>Identifier.</summary>
    [JsonPropertyName("id")] public Guid Id { get; init; }

    /// <summary>Display name.</summary>
    [JsonPropertyName("displayName")] public string? DisplayName { get; init; }
}
