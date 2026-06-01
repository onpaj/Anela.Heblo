using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

/// <summary>
/// Mock implementation of IOneDriveService for use in mock authentication mode (local dev and testing).
/// Returns empty results so the ingestion job runs without errors.
/// </summary>
public class MockOneDriveService : IOneDriveService
{
    private readonly ILogger<MockOneDriveService> _logger;

    public MockOneDriveService(ILogger<MockOneDriveService> logger)
    {
        _logger = logger;
    }

    public Task<List<OneDriveFile>> ListInboxFilesAsync(string driveId, string inboxPath, CancellationToken ct = default)
    {
        _logger.LogInformation("Mock OneDriveService: returning empty inbox file list for drive {DriveId} path {Path}", driveId, inboxPath);
        return Task.FromResult(new List<OneDriveFile>());
    }

    public Task<byte[]> DownloadFileAsync(string driveId, string fileId, CancellationToken ct = default)
    {
        _logger.LogInformation("Mock OneDriveService: simulated download for file {FileId} in drive {DriveId}", fileId, driveId);
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task<string> MoveToArchivedAsync(string driveId, string fileId, string filename, string archivedPath, CancellationToken ct = default)
    {
        _logger.LogInformation("Mock OneDriveService: simulated archive for file {Filename} to {Path} in drive {DriveId}", filename, archivedPath, driveId);
        return Task.FromResult($"https://mock.sharepoint.com/archived/{filename}");
    }

    public Task<string> DownloadFileTextByPathAsync(string driveId, string path, CancellationToken ct = default)
    {
        _logger.LogInformation("Mock OneDriveService: returning canned text for path {Path} in drive {DriveId}", path, driveId);
        return Task.FromResult($"# Mock style guide for {path}\n\nWrite clearly and concisely.");
    }
}
