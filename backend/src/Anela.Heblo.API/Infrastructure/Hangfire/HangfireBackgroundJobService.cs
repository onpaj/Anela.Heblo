using Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Domain.Features.Configuration;
using Hangfire;
using Anela.Heblo.Xcc.Telemetry;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.API.Services;

public class HangfireBackgroundJobService
{
    private readonly ILogger<HangfireBackgroundJobService> _logger;
    private readonly ITelemetryService _telemetryService;
    private readonly IMediator _mediator;
    private readonly IOptions<ProductExportOptions> _productExportOptions;

    public HangfireBackgroundJobService(
        ILogger<HangfireBackgroundJobService> logger,
        ITelemetryService telemetryService,
        IMediator mediator,
        IOptions<ProductExportOptions> productExportOptions)
    {
        _logger = logger;
        _telemetryService = telemetryService;
        _mediator = mediator;
        _productExportOptions = productExportOptions;
    }

    /// <summary>
    /// Daily purchase price recalculation job (runs at 2:00 AM UTC)
    /// </summary>
    [Queue("heblo")]
    public async Task RecalculatePurchasePricesAsync()
    {
        try
        {
            _logger.LogInformation("Starting daily purchase price recalculation job at {Timestamp}", DateTime.UtcNow);

            var request = new RecalculatePurchasePriceRequest
            {
                RecalculateAll = true
            };

            var response = await _mediator.Send(request);

            _logger.LogInformation("Purchase price recalculation completed - Success: {SuccessCount}, Failed: {FailedCount}, Total: {TotalCount}",
                response.SuccessCount, response.FailedCount, response.TotalCount);

            _telemetryService.TrackBusinessEvent("PurchasePriceRecalculation", new Dictionary<string, string>
            {
                ["Status"] = "Success",
                ["SuccessCount"] = response.SuccessCount.ToString(),
                ["FailedCount"] = response.FailedCount.ToString(),
                ["TotalCount"] = response.TotalCount.ToString(),
                ["Timestamp"] = DateTime.UtcNow.ToString("O")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Purchase price recalculation failed at {Timestamp}", DateTime.UtcNow);
            _telemetryService.TrackException(ex, new Dictionary<string, string>
            {
                ["Job"] = "PurchasePriceRecalculation",
                ["Timestamp"] = DateTime.UtcNow.ToString("O")
            });
            throw;
        }
    }

    /// <summary>
    /// Weekly product export download job (runs every Sunday at 2:00 AM UTC)
    /// </summary>
    [Queue("heblo")]
    public async Task DownloadProductExportAsync()
    {
        try
        {
            var exportUrl = _productExportOptions.Value.Url;
            if (string.IsNullOrEmpty(exportUrl))
            {
                _logger.LogWarning("Product export URL is not configured. Skipping product export download job at {Timestamp}", DateTime.UtcNow);
                return;
            }

            var timestamp = DateTime.UtcNow;
            var fileName = $"products_{timestamp:yy_MM_dd_HH_mm}.csv";

            _logger.LogInformation("Starting weekly product export download job at {Timestamp}. Downloading from {Url} as {FileName}",
                timestamp, exportUrl, fileName);

            var request = new DownloadFromUrlRequest
            {
                FileUrl = exportUrl,
                ContainerName = _productExportOptions.Value.ContainerName,
                BlobName = fileName
            };

            var response = await _mediator.Send(request);

            _logger.LogInformation("Product export download completed successfully. File saved as {BlobName} with URL: {BlobUrl}",
                response.BlobName, response.BlobUrl);

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
            _logger.LogError(ex, "Product export download failed at {Timestamp}", DateTime.UtcNow);
            _telemetryService.TrackException(ex, new Dictionary<string, string>
            {
                ["Job"] = "ProductExportDownload",
                ["Timestamp"] = DateTime.UtcNow.ToString("O")
            });
            throw;
        }
    }
}