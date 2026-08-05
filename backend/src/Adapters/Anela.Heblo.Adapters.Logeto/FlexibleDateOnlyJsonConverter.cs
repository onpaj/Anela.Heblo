using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.Logeto;

/// <summary>
/// Accepts both a bare "yyyy-MM-dd" date string and a full ISO datetime string with a
/// time component (e.g. "2026-07-27T00:00:00"), taking just the date part. The real
/// Logeto account returns the TimeTracking item's "Date" field as a full datetime with
/// a midnight time component, which System.Text.Json's built-in DateOnly converter
/// rejects outright. See docs/superpowers/specs/2026-08-05-logeto-spike-results.md,
/// Finding 3.
/// </summary>
public class FlexibleDateOnlyJsonConverter : JsonConverter<DateOnly>
{
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString()
            ?? throw new JsonException("Expected a date string but got null.");

        var datePart = value.Length > 10 ? value[..10] : value;
        return DateOnly.ParseExact(datePart, "yyyy-MM-dd");
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-dd"));
    }
}
