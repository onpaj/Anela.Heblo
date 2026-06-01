using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

internal class GraphDriveItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("webUrl")]
    public string WebUrl { get; set; } = string.Empty;

    [JsonPropertyName("file")]
    public GraphFileFacet? File { get; set; }
}

internal class GraphFileFacet
{
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "application/octet-stream";
}

internal class GraphDriveItemCollection
{
    [JsonPropertyName("value")]
    public List<GraphDriveItem> Value { get; set; } = [];
}
