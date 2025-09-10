using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.FileStorage.Services;

/// <summary>
/// Azure Blob Storage implementation of IBlobStorageService
/// </summary>
public class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(
        BlobServiceClient blobServiceClient,
        HttpClient httpClient,
        ILogger<AzureBlobStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> DownloadFromUrlAsync(string fileUrl, string containerName, string? blobName = null, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting download from URL: {FileUrl}", fileUrl);

            // Download file from URL
            using var response = await _httpClient.GetAsync(fileUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            // Generate blob name if not provided
            if (string.IsNullOrEmpty(blobName))
            {
                var uri = new Uri(fileUrl);
                blobName = Path.GetFileName(uri.LocalPath);

                // If no filename in URL, generate one
                if (string.IsNullOrEmpty(blobName))
                {
                    var extension = GetExtensionFromContentType(response.Content.Headers.ContentType?.MediaType);
                    blobName = $"downloaded-file-{Guid.NewGuid()}{extension}";
                }
            }

            // Get content type from response or infer from extension
            var contentType = response.Content.Headers.ContentType?.MediaType ?? GetContentTypeFromExtension(blobName);

            // Upload to blob storage
            var blobUrl = await UploadAsync(stream, containerName, blobName, contentType, cancellationToken);

            _logger.LogInformation("Successfully downloaded from URL and uploaded to blob: {BlobUrl}", blobUrl);
            return blobUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading from URL {FileUrl} and uploading to container {ContainerName}", fileUrl, containerName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(Stream stream, string containerName, string blobName, string contentType, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Uploading blob {BlobName} to container {ContainerName}", blobName, containerName);

            var containerClient = await GetOrCreateContainerAsync(containerName, cancellationToken);
            var blobClient = containerClient.GetBlobClient(blobName);

            var blobHttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType
            };

            await blobClient.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = blobHttpHeaders
            }, cancellationToken);

            var blobUrl = blobClient.Uri.ToString();
            _logger.LogInformation("Successfully uploaded blob: {BlobUrl}", blobUrl);

            return blobUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading blob {BlobName} to container {ContainerName}", blobName, containerName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting blob {BlobName} from container {ContainerName}", blobName, containerName);

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var response = await containerClient.DeleteBlobIfExistsAsync(blobName, cancellationToken: cancellationToken);

            _logger.LogInformation("Blob deletion result: {WasDeleted}", response.Value);
            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting blob {BlobName} from container {ContainerName}", blobName, containerName);
            throw;
        }
    }

    /// <inheritdoc />
    public string GetBlobUrl(string containerName, string blobName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        return blobClient.Uri.ToString();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);
            var response = await blobClient.ExistsAsync(cancellationToken);
            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if blob {BlobName} exists in container {ContainerName}", blobName, containerName);
            throw;
        }
    }

    private async Task<BlobContainerClient> GetOrCreateContainerAsync(string containerName, CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        return containerClient;
    }

    private static string GetExtensionFromContentType(string? contentType)
    {
        return contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "application/pdf" => ".pdf",
            "text/plain" => ".txt",
            "application/json" => ".json",
            "application/xml" => ".xml",
            _ => ".bin"
        };
    }

    private static string GetContentTypeFromExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
    }
}