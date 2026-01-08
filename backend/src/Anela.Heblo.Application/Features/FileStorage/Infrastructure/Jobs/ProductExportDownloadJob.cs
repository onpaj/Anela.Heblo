using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Xcc.Telemetry;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.FileStorage.Infrastructure.Jobs;

public class ProductExportDownloadJob : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<ProductExportDownloadJob> _logger;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ITelemetryService _telemetryService;
    private readonly IOptions<ProductExportOptions> _productExportOptions;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "product-export-download",
        DisplayName = "Product Export Download",
        Description = "Downloads product export data from external systems",
        CronExpression = "0 2 * * *", // Daily at 2:00 AM
        DefaultIsEnabled = true
    };

    public ProductExportDownloadJob(
        IMediator mediator,
        ILogger<ProductExportDownloadJob> logger,
        IRecurringJobStatusChecker statusChecker,
        ITelemetryService telemetryService,
        IOptions<ProductExportOptions> productExportOptions)
    {
        _mediator = mediator;
        _logger = logger;
        _statusChecker = statusChecker;
        _telemetryService = telemetryService;
        _productExportOptions = productExportOptions;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        try
        {
            var exportUrl = _productExportOptions.Value.Url;
            if (string.IsNullOrEmpty(exportUrl))
            {
                throw new InvalidOperationException("Product export URL is not configured");
            }

            var timestamp = DateTime.UtcNow;
            var fileName = $"products_{timestamp:yy_MM_dd_HH_mm}.csv";

            _logger.LogInformation("Starting {JobName} at {Timestamp}. Downloading from {Url} as {FileName}",
                Metadata.JobName, timestamp, exportUrl, fileName);

            var request = new DownloadFromUrlRequest
            {
                FileUrl = exportUrl,
                ContainerName = _productExportOptions.Value.ContainerName,
                BlobName = fileName
            };

            var response = await _mediator.Send(request, cancellationToken);

            _logger.LogInformation("{JobName} completed successfully. File saved as {BlobName} with URL: {BlobUrl}",
                Metadata.JobName, response.BlobName, response.BlobUrl);

            _telemetryService.TrackBusinessEvent("ProductExportDownload", new Dictionary<string, string>
            {
                ["Status"] = "Success",
                ["FileName"] = fileName,
                ["BlobUrl"] = response.BlobUrl,
                ["FileSize"] = response.FileSizeBytes.ToString(),
                ["Timestamp"] = timestamp.ToString("O")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed at {Timestamp}: {Reason}", Metadata.JobName, DateTime.UtcNow, ex.Message);
            _telemetryService.TrackException(ex, new Dictionary<string, string>
            {
                ["Job"] = Metadata.JobName,
                ["Timestamp"] = DateTime.UtcNow.ToString("O")
            });
            throw;
        }
    }
}
