using Anela.Heblo.Domain.Features.FileStorage;

namespace Anela.Heblo.Tests.Features.FileStorage;

/// <summary>
/// Mock implementation of IBlobStorageService for unit testing
/// </summary>
public class MockBlobStorageService : IBlobStorageService
{
    private readonly Dictionary<string, Dictionary<string, MockBlobInfo>> _containers = new();
    private readonly bool _simulateErrors;

    public MockBlobStorageService(bool simulateErrors = false)
    {
        _simulateErrors = simulateErrors;
    }

    public Task<string> DownloadFromUrlAsync(string fileUrl, string containerName, string? blobName = null, CancellationToken cancellationToken = default)
    {
        if (_simulateErrors)
        {
            throw new HttpRequestException("Simulated download error");
        }

        // Generate blob name if not provided
        if (string.IsNullOrEmpty(blobName))
        {
            var uri = new Uri(fileUrl);
            blobName = Path.GetFileName(uri.LocalPath);
            
            if (string.IsNullOrEmpty(blobName))
            {
                blobName = $"downloaded-file-{Guid.NewGuid()}.bin";
            }
        }

        // Ensure container exists
        if (!_containers.ContainsKey(containerName))
        {
            _containers[containerName] = new Dictionary<string, MockBlobInfo>();
        }

        // Simulate file download and upload
        var blobUrl = $"https://mockstorageaccount.blob.core.windows.net/{containerName}/{blobName}";
        _containers[containerName][blobName] = new MockBlobInfo
        {
            BlobName = blobName,
            ContainerName = containerName,
            Url = blobUrl,
            ContentType = GetContentTypeFromUrl(fileUrl),
            Size = 1024, // Mock size
            CreatedAt = DateTime.UtcNow,
            SourceUrl = fileUrl
        };

        return Task.FromResult(blobUrl);
    }

    public Task<string> UploadAsync(Stream stream, string containerName, string blobName, string contentType, CancellationToken cancellationToken = default)
    {
        if (_simulateErrors)
        {
            throw new InvalidOperationException("Simulated upload error");
        }

        // Ensure container exists
        if (!_containers.ContainsKey(containerName))
        {
            _containers[containerName] = new Dictionary<string, MockBlobInfo>();
        }

        var blobUrl = $"https://mockstorageaccount.blob.core.windows.net/{containerName}/{blobName}";
        _containers[containerName][blobName] = new MockBlobInfo
        {
            BlobName = blobName,
            ContainerName = containerName,
            Url = blobUrl,
            ContentType = contentType,
            Size = stream.Length,
            CreatedAt = DateTime.UtcNow
        };

        return Task.FromResult(blobUrl);
    }

    public Task<bool> DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        if (!_containers.ContainsKey(containerName))
        {
            return Task.FromResult(false);
        }

        var removed = _containers[containerName].Remove(blobName);
        return Task.FromResult(removed);
    }

    public string GetBlobUrl(string containerName, string blobName)
    {
        return $"https://mockstorageaccount.blob.core.windows.net/{containerName}/{blobName}";
    }

    public Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        if (!_containers.ContainsKey(containerName))
        {
            return Task.FromResult(false);
        }

        var exists = _containers[containerName].ContainsKey(blobName);
        return Task.FromResult(exists);
    }

    // Helper methods for testing
    public MockBlobInfo? GetBlob(string containerName, string blobName)
    {
        if (_containers.ContainsKey(containerName) && _containers[containerName].ContainsKey(blobName))
        {
            return _containers[containerName][blobName];
        }
        return null;
    }

    public int GetBlobCount(string containerName)
    {
        return _containers.ContainsKey(containerName) ? _containers[containerName].Count : 0;
    }

    public IEnumerable<string> GetContainerNames()
    {
        return _containers.Keys;
    }

    public void Clear()
    {
        _containers.Clear();
    }

    private static string GetContentTypeFromUrl(string url)
    {
        var extension = Path.GetExtension(new Uri(url).LocalPath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };
    }
}

public class MockBlobInfo
{
    public string BlobName { get; set; } = null!;
    public string ContainerName { get; set; } = null!;
    public string Url { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? SourceUrl { get; set; }
}