using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Services;

internal class NoOpCatalogDocumentsStorage : ICatalogDocumentsStorage
{
    public Task<FolderSearchResult> FindFolderAsync(
        string driveId, string basePath, string prefix, bool allowMultiple, CancellationToken ct = default)
        => Task.FromResult(new FolderSearchResult { Status = FolderStatus.NotFound });

    public Task<List<CatalogDocumentDto>> ListFilesAsync(
        string driveId, string folderId, CancellationToken ct = default)
        => Task.FromResult(new List<CatalogDocumentDto>());

    public Task<string> UploadFileAsync(
        string driveId, string folderId, string filename,
        Stream content, string contentType, long sizeBytes, CancellationToken ct = default)
        => Task.FromResult(filename);
}
