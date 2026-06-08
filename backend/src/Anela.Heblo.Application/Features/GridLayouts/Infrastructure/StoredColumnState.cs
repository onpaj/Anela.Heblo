using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.GridLayouts.Infrastructure;

// Persistence-internal shape for GridLayouts.LayoutJson columns; do NOT add API-only fields here.
internal sealed record StoredColumnState(
    [property: JsonPropertyName("id")]     string Id,
    [property: JsonPropertyName("order")]  int    Order,
    [property: JsonPropertyName("width")]  int?   Width,
    [property: JsonPropertyName("hidden")] bool   Hidden);
