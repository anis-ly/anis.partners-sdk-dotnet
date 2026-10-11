using System.Text.Json.Serialization;

namespace Anis.Partners.Sdk.Models;

/// <summary>The application's current suspended state.</summary>
public sealed record ApplicationSuspended
{
    /// <summary>The admitted application.</summary>
    [JsonPropertyName("applicationId")] public Guid ApplicationId { get; init; }

    /// <summary>Always <c>suspended</c>.</summary>
    [JsonPropertyName("status")] public string? Status { get; init; }

    /// <summary>The suspension instant.</summary>
    [JsonPropertyName("suspendedAt")] public DateTimeOffset SuspendedAt { get; init; }
}

/// <summary>The key's current revoked state.</summary>
public sealed record KeyRevoked
{
    /// <summary>The revoked key.</summary>
    [JsonPropertyName("keyId")] public Guid KeyId { get; init; }

    /// <summary>Always <c>revoked</c>.</summary>
    [JsonPropertyName("status")] public string? Status { get; init; }

    /// <summary>The revocation instant.</summary>
    [JsonPropertyName("revokedAt")] public DateTimeOffset RevokedAt { get; init; }
}

internal sealed record AccessStopRequest
{
    [JsonPropertyName("reason")] public required string Reason { get; init; }
}
