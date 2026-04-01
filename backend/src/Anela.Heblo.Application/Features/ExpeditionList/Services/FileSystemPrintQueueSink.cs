using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionList.Services;

public class FileSystemPrintQueueSink : IPrintQueueSink
{
    private readonly IOptions<PrintPickingListOptions> _options;
    private readonly ILogger<FileSystemPrintQueueSink> _logger;

    public FileSystemPrintQueueSink(
        IOptions<PrintPickingListOptions> options,
        ILogger<FileSystemPrintQueueSink> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var folder = _options.Value.PrintQueueFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            _logger.LogWarning("PrintQueueFolder is not configured. Skipping printer queue copy.");
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(folder);

        foreach (var f in filePaths)
        {
            var fileName = Path.GetFileName(f);
            if (string.IsNullOrEmpty(fileName))
            {
                _logger.LogWarning("Skipping file with invalid path: {FilePath}", f);
                continue;
            }

            File.Copy(f, Path.Combine(folder, fileName));
        }

        return Task.CompletedTask;
    }
}
