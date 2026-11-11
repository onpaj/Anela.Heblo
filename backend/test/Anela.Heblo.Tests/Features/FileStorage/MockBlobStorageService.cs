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

    public async Task<IReadOnlyList<BlobItemInfo>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken = default)
    {
        if (!_containers.ContainsKey(containerName))
        {
            return new List<BlobItemInfo>().AsReadOnly();
        }

        var blobs = _containers[containerName]
            .Values
            .Where(b => prefix == null || b.BlobName.StartsWith(prefix))
            .Select(b => new BlobItemInfo
            {
                Name = b.BlobName,
                FileName = Path.GetFileName(b.BlobName),
                CreatedOn = new DateTimeOffset(b.CreatedAt)
            })
            .ToList()
            .AsReadOnly();

        return blobs;
    }

    public async Task<Stream> DownloadAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        if (!_containers.ContainsKey(containerName) || !_containers[containerName].ContainsKey(blobName))
        {
            throw new InvalidOperationException($"Blob {blobName} not found in container {containerName}");
        }

        // Return mock binary data as a stream
        var content = System.Text.Encoding.UTF8.GetBytes($"Mock content for {blobName}");
        return new MemoryStream(content);
    }

    public Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken cancellationToken = default)
    {
        if (_simulateErrors)
        {
            throw new InvalidOperationException("Simulated list virtual directories error");
        }

        if (!_containers.ContainsKey(containerName))
        {
            IReadOnlyList<string> empty = new List<string>().AsReadOnly();
            return Task.FromResult(empty);
        }

        // Mirror Azure's GetBlobsByHierarchyAsync(delimiter: "/") semantics:
        //   - skip keys that contain no "/" (loose top-level blobs)
        //   - take the substring before the first "/" (first-level segment only)
        //   - distinct
        IReadOnlyList<string> prefixes = _containers[containerName]
            .Keys
            .Select(name =>
            {
                var slash = name.IndexOf('/');
                return slash < 0 ? null : name[..slash];
            })
            .Where(prefix => prefix is not null)
            .Select(prefix => prefix!)
            .Distinct()
            .ToList()
            .AsReadOnly();

        return Task.FromResult(prefixes);
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