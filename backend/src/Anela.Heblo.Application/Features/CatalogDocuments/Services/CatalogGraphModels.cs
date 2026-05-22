using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Services;

internal class CatalogGraphDriveItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("webUrl")]
    public string WebUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTime LastModifiedDateTime { get; set; }

    [JsonPropertyName("file")]
    public CatalogGraphFileFacet? File { get; set; }

    [JsonPropertyName("folder")]
    public CatalogGraphFolderFacet? Folder { get; set; }
}

internal class CatalogGraphFileFacet
{
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "application/octet-stream";
}

internal class CatalogGraphFolderFacet
{
    [JsonPropertyName("childCount")]
    public int ChildCount { get; set; }
}

internal class CatalogGraphDriveItemCollection
{
    [JsonPropertyName("value")]
    public List<CatalogGraphDriveItem> Value { get; set; } = [];
}

internal class CatalogGraphUploadSession
{
    [JsonPropertyName("uploadUrl")]
    public string UploadUrl { get; set; } = string.Empty;
}
