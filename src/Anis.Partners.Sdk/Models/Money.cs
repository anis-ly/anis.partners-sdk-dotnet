using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anis.Partners.Sdk.Models;

/// <summary>An amount and its currency.</summary>
/// <remarks>
/// The wire form is a DECIMAL STRING with scale exactly three, never a JSON number. That is deliberate on
/// the contract's part and preserved here: a double round trip through 21.000 is how a partner ends up
/// sending an expectedTotal the gateway refuses as not equal to unit times quantity.
///
/// In C# it is a <see cref="decimal"/>, and <see cref="Multiply"/> is exact integer arithmetic on that
/// decimal, so a total computed from a unit price cannot drift.
/// </remarks>
[JsonConverter(typeof(MoneyConverter))]
public readonly record struct Money(decimal Amount, string Currency, DateTimeOffset? AsOf = null)
{
    /// <summary>The wire representation: scale exactly three.</summary>
    public string ToWireAmount() => Amount.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>Multiplies by a whole quantity, exactly.</summary>
    public Money Multiply(int quantity) => this with { Amount = Amount * quantity, AsOf = null };

    /// <inheritdoc/>
    public override string ToString() => $"{ToWireAmount()} {Currency}";
}

/// <summary>Reads and writes <see cref="Money"/> in the contract's exact form.</summary>
internal sealed class MoneyConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var amount = 0m;
        string? currency = null;
        DateTimeOffset? asOf = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var name = reader.GetString();
            reader.Read();

            switch (name)
            {
                case "amount":
                    // Parsed from the string, never from a number: the contract never sends one, and
                    // accepting one here would quietly admit a double that has already lost precision.
                    amount = decimal.Parse(reader.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture);
                    break;

                case "currency":
                    currency = reader.GetString();
                    break;

                case "asOf":
                    asOf = reader.TokenType is JsonTokenType.Null ? null : reader.GetDateTimeOffset();
                    break;
            }
        }

        return new Money(amount, currency ?? string.Empty, asOf);
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartObject();
        writer.WriteString("amount", value.ToWireAmount());
        writer.WriteString("currency", value.Currency);

        if (value.AsOf is { } asOf)
            writer.WriteString("asOf", asOf.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

        writer.WriteEndObject();
    }
}
