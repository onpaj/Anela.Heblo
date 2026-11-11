using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Azure.Features.FileStorage;

/// <summary>
/// Azure Blob Storage implementation of IBlobStorageService
/// </summary>
public sealed class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<AzureBlobStorageService> _logger;
    private readonly ConcurrentDictionary<string, bool> _containerExists = new();

    public AzureBlobStorageService(
        BlobServiceClient blobServiceClient,
        ILogger<AzureBlobStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
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

            // BlobUploadOptions without Conditions performs an unconditional PUT — always overwrites.
            // (Conditions.IfNoneMatch = ETag.All would prevent overwriting; we intentionally omit it.)
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlobItemInfo>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var items = new List<BlobItemInfo>();

            await foreach (var blob in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
            {
                items.Add(new BlobItemInfo
                {
                    Name = blob.Name,
                    FileName = Path.GetFileName(blob.Name),
                    CreatedOn = blob.Properties.CreatedOn,
                    ContentLength = blob.Properties.ContentLength
                });
            }

            return items.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing blobs in container {ContainerName} with prefix {Prefix}", containerName, prefix);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Downloading blob {BlobName} from container {ContainerName}", blobName, containerName);
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);
            var download = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return new BlobDownloadStream(download.Value.Content, download.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning(ex, "Blob {BlobName} not found in container {ContainerName}", blobName, containerName);
            throw new FileNotFoundException($"Blob '{blobName}' not found in container '{containerName}'.", blobName, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading blob {BlobName} from container {ContainerName}", blobName, containerName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(
        string containerName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var prefixes = new List<string>();

            // Use named arguments: the positional order is (BlobTraits, BlobStates, string delimiter, string prefix, CancellationToken),
            // so a naive caller writing (null, "/") positionally would pass null as delimiter and "/" as prefix — the opposite of intent.
            // Named arguments make the intent unambiguous.
            // No GetOrCreateContainerAsync — matches ListBlobsAsync behaviour.
            await foreach (var item in containerClient.GetBlobsByHierarchyAsync(
                prefix: null,
                delimiter: "/",
                cancellationToken: cancellationToken))
            {
                if (item.IsPrefix)
                {
                    var name = item.Prefix;
                    if (name.EndsWith('/'))
                    {
                        name = name[..^1];
                    }
                    prefixes.Add(name);
                }
            }

            return prefixes.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing virtual directories in container {ContainerName}", containerName);
            throw;
        }
    }

    /// <summary>
    /// Wraps a blob streaming download to ensure the underlying Azure SDK response is disposed
    /// together with the content stream, preventing connection leaks.
    /// </summary>
    private sealed class BlobDownloadStream(Stream content, IDisposable owner) : Stream
    {
        public override bool CanRead => content.CanRead;
        public override bool CanSeek => content.CanSeek;
        public override bool CanWrite => content.CanWrite;
        public override long Length => content.Length;
        public override long Position { get => content.Position; set => content.Position = value; }
        public override void Flush() => content.Flush();
        public override int Read(byte[] buffer, int offset, int count) => content.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => content.Seek(offset, origin);
        public override void SetLength(long value) => content.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => content.Write(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            content.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            content.ReadAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                content.Dispose();
                owner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private async Task<BlobContainerClient> GetOrCreateContainerAsync(string containerName, CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        if (!_containerExists.ContainsKey(containerName))
        {
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
            _containerExists.TryAdd(containerName, true);
        }

        return containerClient;
    }
}