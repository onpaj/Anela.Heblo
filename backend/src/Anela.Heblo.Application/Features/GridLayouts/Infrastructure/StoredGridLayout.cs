using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.GridLayouts.Infrastructure;

// Persistence-internal shape for GridLayouts.LayoutJson; do NOT add API-only fields here.
internal sealed record StoredGridLayout(
    [property: JsonPropertyName("columns")] List<StoredColumnState> Columns)
{
    public StoredGridLayout() : this(new List<StoredColumnState>()) { }
}
