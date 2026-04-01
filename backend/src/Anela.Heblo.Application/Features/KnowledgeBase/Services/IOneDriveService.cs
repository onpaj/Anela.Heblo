namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public record OneDriveFile(string Id, string Name, string ContentType, string Path);

public interface IOneDriveService
{
    Task<List<OneDriveFile>> ListInboxFilesAsync(string driveId, string inboxPath, CancellationToken ct = default);
    Task<byte[]> DownloadFileAsync(string driveId, string fileId, CancellationToken ct = default);
    Task MoveToArchivedAsync(string driveId, string fileId, string filename, string archivedPath, CancellationToken ct = default);
}
