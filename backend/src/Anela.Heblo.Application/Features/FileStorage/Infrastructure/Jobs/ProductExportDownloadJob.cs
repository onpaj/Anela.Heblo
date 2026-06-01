using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Xcc.Telemetry;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.FileStorage.Infrastructure.Jobs;

[AutomaticRetry(Attempts = 0)]
public sealed class ProductExportDownloadJob : IRecurringJob
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
        CronExpression = "0 2 * * *",
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
            _telemetryService.TrackBusinessEvent("ProductExportDownload", new Dictionary<string, string>
            {
                ["Status"] = "Skipped",
            });
            return;
        }

        var exportUrl = _productExportOptions.Value.Url;
        if (string.IsNullOrEmpty(exportUrl))
        {
            throw new InvalidOperationException("Product export URL is not configured");
        }

        var timestamp = DateTime.UtcNow;
        var fileName = $"products_{timestamp:yy_MM_dd_HH_mm}.csv";

        _logger.LogInformation(
            "Starting {JobName} at {Timestamp}. Downloading from {Url} as {FileName}",
            Metadata.JobName, timestamp, exportUrl, fileName);

        var sw = Stopwatch.StartNew();
        DownloadFromUrlResponse? response = null;

        try
        {
            response = await _mediator.Send(new DownloadFromUrlRequest
            {
                FileUrl = exportUrl,
                ContainerName = _productExportOptions.Value.ContainerName,
                BlobName = fileName,
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogInformation("Job {JobName} was cancelled.", Metadata.JobName);
            _telemetryService.TrackBusinessEvent("ProductExportDownload", new Dictionary<string, string>
            {
                ["Status"] = "Cancelled",
                ["ElapsedMs"] = sw.ElapsedMilliseconds.ToString(),
            });
            throw;
        }

        sw.Stop();
        var elapsedMs = sw.ElapsedMilliseconds.ToString();
        var attemptCount = response?.Params != null && response.Params.TryGetValue("attemptCount", out var ac)
            ? ac
            : "1";

        if (response is { Success: true })
        {
            _logger.LogInformation(
                "{JobName} completed successfully. File: {FileName}, Blob: {BlobUrl}, Size: {Size}",
                Metadata.JobName, response.BlobName, response.BlobUrl, response.FileSizeBytes);

            _telemetryService.TrackBusinessEvent("ProductExportDownload", new Dictionary<string, string>
            {
                ["Status"] = "Success",
                ["AttemptCount"] = attemptCount,
                ["ElapsedMs"] = elapsedMs,
                ["FileName"] = fileName,
                ["BlobUrl"] = response.BlobUrl ?? string.Empty,
                ["FileSize"] = response.FileSizeBytes.ToString(),
            });
            return;
        }

        // Failure path
        _logger.LogError(
            "{JobName} failed. ErrorCode: {ErrorCode}, Params: {Params}",
            Metadata.JobName, response?.ErrorCode, response?.FullError());

        var props = new Dictionary<string, string>
        {
            ["Status"] = "Failed",
            ["AttemptCount"] = attemptCount,
            ["ElapsedMs"] = elapsedMs,
            ["ErrorCode"] = response?.ErrorCode?.ToString() ?? "FileDownloadFailed",
        };
        if (response?.Params != null && response.Params.TryGetValue("cause", out var cause))
        {
            props["Cause"] = cause;
        }

        _telemetryService.TrackBusinessEvent("ProductExportDownload", props);

        // Rethrow so Hangfire records run as Failed. [AutomaticRetry(Attempts=0)] prevents re-execution.
        var causeForMsg = response?.Params != null && response.Params.TryGetValue("cause", out var c) ? c : "unknown";
        throw new InvalidOperationException(
            $"ProductExportDownload failed (cause={causeForMsg}, attempts={attemptCount}, elapsedMs={elapsedMs}).");
    }
}
