using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anis.Partners.Sdk.Models;

/// <summary>Maps the contract's lower-case enum strings to a C# enum, by name, case-insensitively.</summary>
/// <remarks>
/// Lenient in exactly one direction: a value this SDK version does not know deserializes to the enum's
/// zero member rather than throwing. The alternative is an SDK that stops reading a whole response because
/// the owner published a new catalogue category type — a hard failure over a field the caller may not even
/// look at.
///
/// <c>Order.Status</c> uses it too, so an unknown value there surfaces as <see cref="OrderStatus.Unknown"/>. The
/// order outcome does not depend on it: the SDK classifies an order answer by its HTTP status, its credentials and
/// its replay marker.
/// </remarks>
/// <typeparam name="T">The enum type.</typeparam>
internal sealed class LenientEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    /// <inheritdoc/>
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType is JsonTokenType.String && Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var value)
            ? value
            : default;

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var name = value.ToString();

        writer.WriteStringValue(char.ToLowerInvariant(name[0]) + name[1..]);
    }
}
