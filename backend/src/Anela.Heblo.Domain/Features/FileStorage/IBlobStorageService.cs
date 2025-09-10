namespace Anela.Heblo.Domain.Features.FileStorage;

/// <summary>
/// Service interface for blob storage operations
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Downloads a file from the specified URL and uploads it to blob storage
    /// </summary>
    /// <param name="fileUrl">URL of the file to download</param>
    /// <param name="containerName">Name of the blob storage container</param>
    /// <param name="blobName">Optional custom name for the blob. If not provided, will use filename from URL</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>URL of the uploaded blob</returns>
    Task<string> DownloadFromUrlAsync(string fileUrl, string containerName, string? blobName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file stream to blob storage
    /// </summary>
    /// <param name="stream">File stream to upload</param>
    /// <param name="containerName">Name of the blob storage container</param>
    /// <param name="blobName">Name for the blob</param>
    /// <param name="contentType">Content type of the file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>URL of the uploaded blob</returns>
    Task<string> UploadAsync(Stream stream, string containerName, string blobName, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blob from storage
    /// </summary>
    /// <param name="containerName">Name of the blob storage container</param>
    /// <param name="blobName">Name of the blob to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deleted successfully, false if blob didn't exist</returns>
    Task<bool> DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the URL of a blob
    /// </summary>
    /// <param name="containerName">Name of the blob storage container</param>
    /// <param name="blobName">Name of the blob</param>
    /// <returns>URL of the blob</returns>
    string GetBlobUrl(string containerName, string blobName);

    /// <summary>
    /// Checks if a blob exists
    /// </summary>
    /// <param name="containerName">Name of the blob storage container</param>
    /// <param name="blobName">Name of the blob</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if blob exists, false otherwise</returns>
    Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken cancellationToken = default);
}