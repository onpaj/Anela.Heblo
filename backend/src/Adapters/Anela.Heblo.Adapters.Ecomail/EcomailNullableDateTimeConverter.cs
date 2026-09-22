using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.Ecomail;

/// <summary>
/// Ecomail returns timestamps as "2026-07-26 05:33:17" — a space, not "T" — which
/// System.Text.Json's built-in DateTime converter rejects. Applies to
/// EcomailCampaignDto.SentAt and EcomailPipelineDto.CreatedAt/.UpdatedAt.
///
/// Three-tier parse, in order: null/empty stays null (genuinely absent); Ecomail's own
/// space-separated format; a plain DateTime.TryParse fallback (covers ISO-8601, in case
/// Ecomail ever changes shape). Anything else throws — a format change must surface loudly
/// instead of silently nulling out a send date.
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
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            // These columns are `timestamp without time zone`; keep Kind=Unspecified regardless
            // of what this fallback path inferred (e.g. a "Z"-suffixed ISO-8601 string).
            return parsed.Kind == DateTimeKind.Unspecified ? parsed : DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        }

        throw new JsonException($"Ecomail returned a timestamp that could not be parsed: \"{value}\".");
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
