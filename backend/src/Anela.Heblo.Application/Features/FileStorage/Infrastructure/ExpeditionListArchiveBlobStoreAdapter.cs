using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Domain.Features.FileStorage;

namespace Anela.Heblo.Application.Features.FileStorage.Infrastructure;

/// <summary>
/// FileStorage-owned adapter implementing ExpeditionListArchive's IExpeditionListArchiveBlobStore
/// contract by delegating to the existing IBlobStorageService. Pure delegation/mapping — no
/// business logic. Mirrors KnowledgeBaseLeafletSourceAdapter's shape and visibility.
/// </summary>
internal sealed class ExpeditionListArchiveBlobStoreAdapter : IExpeditionListArchiveBlobStore
{
    private readonly IBlobStorageService _blobStorageService;

    public ExpeditionListArchiveBlobStoreAdapter(IBlobStorageService blobStorageService)
    {
        _blobStorageService = blobStorageService;
    }

    public Task<Stream> DownloadAsync(string containerName, string blobPath, CancellationToken cancellationToken)
        => _blobStorageService.DownloadAsync(containerName, blobPath, cancellationToken);

    public async Task<IReadOnlyList<ExpeditionBlobItem>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken)
    {
        var blobs = await _blobStorageService.ListBlobsAsync(containerName, prefix, cancellationToken);
        return blobs
            .Select(b => new ExpeditionBlobItem
            {
                Name = b.Name,
                FileName = b.FileName,
                CreatedOn = b.CreatedOn,
                ContentLength = b.ContentLength,
            })
            .ToList();
    }

    public Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken cancellationToken)
        => _blobStorageService.ListVirtualDirectoriesAsync(containerName, cancellationToken);
}
