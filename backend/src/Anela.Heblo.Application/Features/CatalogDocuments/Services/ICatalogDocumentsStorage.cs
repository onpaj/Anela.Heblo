using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Services;

public interface ICatalogDocumentsStorage
{
    /// <summary>
    /// Lists immediate child folders of basePath and returns the one whose name starts with prefix.
    /// For Material: prefix = "{productCode}__" (exact one folder expected).
    /// For PIF: prefix = "{productCode.Substring(0,6)}__" (multiple = shared folder, OK).
    /// </summary>
    Task<FolderSearchResult> FindFolderAsync(
        string driveId, string basePath, string prefix, bool allowMultiple,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all files (not subfolders) directly inside the folder identified by folderId.
    /// </summary>
    Task<List<CatalogDocumentDto>> ListFilesAsync(
        string driveId, string folderId, CancellationToken ct = default);

    /// <summary>
    /// Uploads a file into the folder identified by folderId.
    /// Uses upload session for files > 4 MB; simple PUT otherwise.
    /// Conflict behavior: rename (new file gets a "(1)"-style suffix).
    /// Returns the final filename as stored in SharePoint.
    /// </summary>
    Task<string> UploadFileAsync(
        string driveId, string folderId, string filename,
        Stream content, string contentType, long sizeBytes,
        CancellationToken ct = default);
}
