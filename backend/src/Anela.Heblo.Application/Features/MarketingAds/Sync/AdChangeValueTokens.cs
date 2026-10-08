using System.Text.Json;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// Every string value inside a change value, however a platform nests it ("\"PAUSED\"",
/// {"status":"PAUSED"}, {"keyword":{"text":"zdarma"}}). Lets the matcher compare platform
/// change logs and snapshot diffs without per-platform parsing.
/// </summary>
public static class AdChangeValueTokens
{
    private const int MaxDepth = 4;

    public static IReadOnlyList<string> Extract(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();
        try
        {
            using var document = JsonDocument.Parse(json);
            var tokens = new List<string>();
            Collect(document.RootElement, 0, tokens);
            return tokens;
        }
        catch (JsonException)
        {
            return new[] { json.Trim() };
        }
    }

    private static void Collect(JsonElement element, int depth, List<string> tokens)
    {
        if (depth > MaxDepth)
            return;
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                tokens.Add(element.GetString()!);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Collect(property.Value, depth + 1, tokens);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, depth + 1, tokens);
                break;
        }
    }
}
