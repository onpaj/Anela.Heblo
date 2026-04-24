namespace Anela.Heblo.Application.Features.Photobank.Services;

public class GraphPhotoItem
{
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string? WebUrl { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string DriveId { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}

public class GraphDeltaResult
{
    public List<GraphPhotoItem> Items { get; set; } = [];
    public string NewDeltaLink { get; set; } = string.Empty;
}

public interface IPhotobankGraphService
{
    Task<GraphDeltaResult> GetDeltaAsync(string driveId, string rootItemId, string? deltaLink, CancellationToken ct = default);
}
