using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.Ecomail;

/// <summary>
/// Ecomail returns timestamps as "2026-07-26 05:33:17" — a space, not "T" — which
/// System.Text.Json's built-in DateTime converter rejects. Applies to
/// EcomailCampaignDto.SentAt and EcomailPipelineDto.CreatedAt/.UpdatedAt.
/// </summary>
public sealed class EcomailNullableDateTimeConverter : JsonConverter<DateTime?>
{
    private const string Format = "yyyy-MM-dd HH:mm:ss";

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return DateTime.TryParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result
            : null;
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value.ToString(Format, CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
