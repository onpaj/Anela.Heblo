namespace Anela.Heblo.Domain.Features.FileStorage;

/// <summary>
/// Metadata about a blob item in storage
/// </summary>
public class BlobItemInfo
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset? CreatedOn { get; set; }
    public long? ContentLength { get; set; }
}

/// <summary>
/// Service interface for blob storage operations
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Downloads a file from the specified URL and uploads it to blob storage
    /// </summary>
    Task<string> DownloadFromUrlAsync(string fileUrl, string containerName, string? blobName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file stream to blob storage
    /// </summary>
    Task<string> UploadAsync(Stream stream, string containerName, string blobName, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blob from storage
    /// </summary>
    Task<bool> DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the URL of a blob
    /// </summary>
    string GetBlobUrl(string containerName, string blobName);

    /// <summary>
    /// Checks if a blob exists
    /// </summary>
    Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists blobs in a container with optional prefix filter
    /// </summary>
    Task<IReadOnlyList<BlobItemInfo>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a blob as a stream
    /// </summary>
    Task<Stream> DownloadAsync(string containerName, string blobName, CancellationToken cancellationToken = default);
}
