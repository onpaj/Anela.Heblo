using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Dashboard.Contracts;

// Plain class (not a record) — DTO serialization rule from CLAUDE.md: OpenAPI client
// generators mishandle record parameter order. Tile payloads use this DTO embedded in
// an anonymous projection because LoadDataAsync returns Task<object>.
public class DashboardTileDrillDown
{
    [JsonPropertyName("routeKey")]
    public string RouteKey { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("parameters")]
    public IReadOnlyDictionary<string, string>? Parameters { get; set; }
}
