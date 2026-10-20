using System.Globalization;
using System.Text.Json;

namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

/// <summary>
/// Reads Google Ads REST rows (proto3 JSON): lowerCamelCase names, int64 values as strings, and
/// fields at their default value (0, false, "") omitted entirely — so a missing metric means zero.
/// </summary>
internal static class GoogleAdsJson
{
    private const decimal MicrosPerUnit = 1_000_000m;

    public static JsonElement? Find(JsonElement row, params string[] path)
    {
        var current = row;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                return null;
            current = next;
        }
        return current;
    }

    public static string? String(JsonElement row, params string[] path) =>
        Find(row, path) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    public static string RequiredString(JsonElement row, params string[] path) =>
        String(row, path)
        ?? throw new InvalidOperationException($"Google Ads row is missing '{string.Join('.', path)}'.");

    public static long Int64(JsonElement row, params string[] path) => Find(row, path) switch
    {
        { ValueKind: JsonValueKind.String } s => long.Parse(s.GetString()!, NumberStyles.Integer, CultureInfo.InvariantCulture),
        { ValueKind: JsonValueKind.Number } n => n.GetInt64(),
        _ => 0,
    };

    public static decimal Decimal(JsonElement row, params string[] path) => Find(row, path) switch
    {
        { ValueKind: JsonValueKind.String } s => decimal.Parse(s.GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture),
        { ValueKind: JsonValueKind.Number } n => decimal.Parse(n.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture),
        _ => 0m,
    };

    public static decimal Micros(JsonElement row, params string[] path) => Int64(row, path) / MicrosPerUnit;

    public static bool Bool(JsonElement row, params string[] path) =>
        Find(row, path) is { ValueKind: JsonValueKind.True };

    public static string? Raw(JsonElement row, params string[] path) => Find(row, path)?.GetRawText();
}
