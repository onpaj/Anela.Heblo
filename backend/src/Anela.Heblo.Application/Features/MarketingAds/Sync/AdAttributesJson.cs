using System.Text.Json;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// ad_entities.attributes is jsonb, which normalises key order and whitespace. Attributes are
/// therefore always compared as parsed dictionaries, never as text.
/// </summary>
public static class AdAttributesJson
{
    private static readonly IReadOnlyDictionary<string, string?> Empty = new Dictionary<string, string?>();

    public static string Serialize(IReadOnlyDictionary<string, string?> attributes) =>
        JsonSerializer.Serialize(new SortedDictionary<string, string?>(
            attributes.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal));

    public static IReadOnlyDictionary<string, string?> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Empty;

            var result = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText(),
                };
            }
            return result;
        }
        catch (JsonException)
        {
            // A corrupt stored value is treated as "no attributes"; the next diff rewrites it once.
            return Empty;
        }
    }

    public static string Subset(IReadOnlyDictionary<string, string?> attributes, IEnumerable<string> keys) =>
        JsonSerializer.Serialize(new SortedDictionary<string, string?>(
            keys.ToDictionary(k => k, k => attributes.TryGetValue(k, out var v) ? v : null), StringComparer.Ordinal));
}
