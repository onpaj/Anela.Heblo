namespace Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

/// <summary>
/// ExpeditionListArchive-owned narrow contract over blob storage. Exposes only the three
/// operations this module actually needs, implemented by a FileStorage-owned adapter
/// (see Anela.Heblo.Application.Features.FileStorage.Infrastructure.ExpeditionListArchiveBlobStoreAdapter).
/// Do not add speculative methods here — extend only when a handler in this module needs them.
/// </summary>
public interface IExpeditionListArchiveBlobStore
{
    Task<Stream> DownloadAsync(string containerName, string blobPath, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExpeditionBlobItem>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken cancellationToken);
}
