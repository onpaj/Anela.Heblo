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

    public Task<List<OneDriveFile>> ListInboxFilesAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Mock OneDriveService: returning empty inbox file list");
        return Task.FromResult(new List<OneDriveFile>());
    }

    public Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default)
    {
        _logger.LogInformation("Mock OneDriveService: simulated download for file {FileId}", fileId);
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task MoveToArchivedAsync(string fileId, string filename, CancellationToken ct = default)
    {
        _logger.LogInformation("Mock OneDriveService: simulated archive for file {Filename}", filename);
        return Task.CompletedTask;
    }
}
