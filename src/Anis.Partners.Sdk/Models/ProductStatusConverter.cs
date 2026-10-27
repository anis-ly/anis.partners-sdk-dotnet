using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anis.Partners.Sdk.Models;

// Product finality depends on these values; Enum.TryParse also accepts numbers and comma combinations.
internal sealed class ProductStatusConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private static readonly Dictionary<string, T> ByWire = Enum.GetValues<T>()
        .ToDictionary(value => JsonNamingPolicy.CamelCase.ConvertName(value.ToString()), StringComparer.Ordinal);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && ByWire.TryGetValue(reader.GetString()!, out var value))
            return value;
        reader.Skip();
        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(ByWire.FirstOrDefault(pair => EqualityComparer<T>.Default.Equals(pair.Value, value)).Key ?? "unknown");
}
