using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.GridLayouts.Contracts;

public class GridLayoutDto
{
    [JsonPropertyName("gridKey")]
    public string GridKey { get; set; } = string.Empty;

    [JsonPropertyName("columns")]
    public List<GridColumnStateDto> Columns { get; set; } = new();

    [JsonPropertyName("lastModified")]
    public DateTime? LastModified { get; set; }
}
