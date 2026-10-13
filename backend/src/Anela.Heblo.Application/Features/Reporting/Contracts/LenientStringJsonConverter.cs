using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Reporting.Contracts;

/// <summary>
/// Reads a JSON string, number (raw text as written) or boolean into a string, so MCP clients that send
/// <c>"value": 100</c> or <c>"value": true</c> do not fail argument binding. Always writes a string.
/// </summary>
public class LenientStringJsonConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                var raw = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray();
                return Encoding.UTF8.GetString(raw);
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.Null:
                return null;
            default:
                throw new JsonException($"Expected a string, number or boolean but found {reader.TokenType}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

/// <summary>List variant of <see cref="LenientStringJsonConverter"/>: every item may be a string, number or boolean.</summary>
public class LenientStringListJsonConverter : JsonConverter<List<string>>
{
    private static readonly LenientStringJsonConverter ItemConverter = new();

    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected an array but found {reader.TokenType}.");

        var items = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            items.Add(ItemConverter.Read(ref reader, typeof(string), options)!);
        return items;
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
