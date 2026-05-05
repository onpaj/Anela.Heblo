namespace Anela.Heblo.Application.Features.Photobank.Services;

public enum ThumbnailSize { Medium, Large }

public sealed record GraphThumbnail(Stream Content, string ContentType, long? ContentLength);

public sealed class GraphThrottledException : Exception
{
    public TimeSpan? RetryAfter { get; }

    public GraphThrottledException(TimeSpan? retryAfter)
        : base("Microsoft Graph API rate limit exceeded (HTTP 429).")
    {
        RetryAfter = retryAfter;
    }
}

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
    Task<string> ResolveItemIdAsync(string driveId, string folderPath, CancellationToken ct = default);
    Task<GraphThumbnail?> GetThumbnailAsync(string driveId, string fileId, ThumbnailSize size, CancellationToken ct = default);
}
