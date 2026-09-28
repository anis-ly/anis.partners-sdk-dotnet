using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anis.Partners.Sdk.Models;

/// <summary>An RFC 9457 problem.</summary>
/// <remarks>
/// <see cref="Code"/> and the documented extension members are the machine contract.
/// <see cref="Title"/> and <see cref="Detail"/> are localized presentation and must not drive branching.
///
/// A problem never carries database, stack, endpoint, signature base, digest, nonce, token, challenge,
/// key, voucher or serial detail, nor another Partner's identity — so it is safe to log whole.
/// </remarks>
public sealed record Problem
{
    /// <summary>The permanent documentation page for this code.</summary>
    [JsonPropertyName("type")] public string? Type { get; init; }

    /// <summary>Localized title. Presentation only.</summary>
    [JsonPropertyName("title")] public string? Title { get; init; }

    /// <summary>The HTTP status, repeated in the body.</summary>
    [JsonPropertyName("status")] public int Status { get; init; }

    /// <summary>The machine code. Branch on this.</summary>
    [JsonPropertyName("code")] public string? Code { get; init; }

    /// <summary>Localized detail. Presentation only.</summary>
    [JsonPropertyName("detail")] public string? Detail { get; init; }

    /// <summary>The correlation id to quote when asking Anis about this call.</summary>
    [JsonPropertyName("requestId")] public string? RequestId { get; init; }

    /// <summary>Documented per-code extension members.</summary>
    [JsonPropertyName("extensions")] public JsonElement? Extensions { get; init; }
}
