using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Anis.Partners.Sdk.Verification;

internal static partial class WebhookTimestamp
{
    internal static bool TryParse(string? text, out DateTimeOffset value)
    {
        value = default;
        return text is not null && Rfc3339().IsMatch(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex Rfc3339();
}

// Invalid lifecycle times become absent metadata so verification reports KeyRejected, not a JSON parser error.
internal sealed class PublishedKeyTimestampConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && WebhookTimestamp.TryParse(reader.GetString(), out var value))
            return value;
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is { } time) writer.WriteStringValue(time);
        else writer.WriteNullValue();
    }
}
