using Anela.Heblo.Application.Shared.Printing;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Azure.Features.ExpeditionList;

public class AzureBlobPrintQueueSink : IPrintQueueSink
{
    private readonly BlobContainerClient _containerClient;
    private readonly TimeProvider _clock;
    private readonly ILogger<AzureBlobPrintQueueSink> _logger;
    private readonly SemaphoreSlim _ensureGate = new(1, 1);
    private bool _containerEnsured;

    public AzureBlobPrintQueueSink(
        BlobContainerClient containerClient,
        TimeProvider clock,
        ILogger<AzureBlobPrintQueueSink> logger)
    {
        _containerClient = containerClient;
        _clock = clock;
        _logger = logger;
    }

    public async Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var files = filePaths.ToList();
        if (files.Count == 0)
            return;

        await EnsureContainerAsync(cancellationToken);
        var datePrefix = _clock.GetLocalNow().ToString("yyyy-MM-dd");

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName))
            {
                _logger.LogWarning("Skipping file with invalid path: {FilePath}", filePath);
                continue;
            }

            var blobName = $"{datePrefix}/{fileName}";
            await using var fileStream = File.OpenRead(filePath);
            var blobClient = _containerClient.GetBlobClient(blobName);
            await blobClient.UploadAsync(fileStream, overwrite: true, cancellationToken: cancellationToken);
            _logger.LogDebug("Uploaded {FileName} to blob {BlobName}", fileName, blobName);
        }
    }

    // Verifies the target blob container exists at most once per process lifetime.
    // The flag is set only after CreateIfNotExistsAsync returns successfully — a transient
    // failure leaves _containerEnsured == false so the next SendAsync retries.
    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_containerEnsured) return;

        await _ensureGate.WaitAsync(cancellationToken);
        try
        {
            if (_containerEnsured) return;

            _logger.LogDebug("Ensuring blob container exists for print queue sink");
            await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _containerEnsured = true;
        }
        finally
        {
            _ensureGate.Release();
        }
    }
}
