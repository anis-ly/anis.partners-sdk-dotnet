using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anis.Partners.Sdk;

/// <summary>The one serializer configuration the whole SDK uses.</summary>
/// <remarks>
/// One configuration, in one place, because the digest is taken over serialized bytes: two settings
/// objects that differ by a naming policy would produce two different bodies for the same request, and the
/// one that was not signed is the one that goes out.
/// </remarks>
internal static class AnisJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // The surface names every member explicitly with [JsonPropertyName]; leaving the policy null keeps
        // that the single source of the wire spelling.
        PropertyNamingPolicy = null,
    };
}
