using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics.Model;

/// <summary>
/// Shoptet stamps order times as "2026-09-21T20:36:22+0200" — a basic-format UTC offset with no
/// colon. System.Text.Json only accepts the extended form ("+02:00") and throws on the rest, so
/// the value is parsed with the general DateTimeOffset parser instead.
/// </summary>
public sealed class ShoptetDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        throw new JsonException($"Unrecognised Shoptet date-time value '{text}'.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value);
        else
            writer.WriteNullValue();
    }
}
