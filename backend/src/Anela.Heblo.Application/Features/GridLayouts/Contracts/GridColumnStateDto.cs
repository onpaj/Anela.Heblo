using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.GridLayouts.Contracts;

public class GridColumnStateDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; }
}
