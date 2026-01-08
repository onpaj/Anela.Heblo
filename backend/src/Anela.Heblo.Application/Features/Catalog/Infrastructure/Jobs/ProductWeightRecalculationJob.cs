using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs;

public class ProductWeightRecalculationJob : IRecurringJob
{
    private readonly IProductWeightRecalculationService _service;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<ProductWeightRecalculationJob> _logger;
    private readonly ITelemetryService _telemetryService;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "product-weight-recalculation",
        DisplayName = "Product Weight Recalculation",
        Description = "Recalculates product weights based on current material composition",
        CronExpression = "0 2 * * *", // Daily at 2:00 AM
        DefaultIsEnabled = true
    };

    public ProductWeightRecalculationJob(
        IProductWeightRecalculationService service,
        IRecurringJobStatusChecker statusChecker,
        ILogger<ProductWeightRecalculationJob> logger,
        ITelemetryService telemetryService)
    {
        _service = service;
        _statusChecker = statusChecker;
        _logger = logger;
        _telemetryService = telemetryService;
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
            _logger.LogInformation("Starting {JobName} at {Timestamp}", Metadata.JobName, DateTime.UtcNow);

            var result = await _service.RecalculateAllProductWeights();

            _logger.LogInformation("{JobName} completed at {Timestamp}. Success: {SuccessCount}, Errors: {ErrorCount}, Duration: {Duration}",
                Metadata.JobName, DateTime.UtcNow, result.SuccessCount, result.ErrorCount, result.Duration);

            _telemetryService.TrackBusinessEvent("ProductWeightRecalculation", new Dictionary<string, string>
            {
                ["Status"] = result.ErrorCount == 0 ? "Success" : "PartialSuccess",
                ["ProcessedCount"] = result.ProcessedCount.ToString(),
                ["SuccessCount"] = result.SuccessCount.ToString(),
                ["ErrorCount"] = result.ErrorCount.ToString(),
                ["Duration"] = result.Duration.ToString(),
                ["StartTime"] = result.StartTime.ToString("O"),
                ["EndTime"] = result.EndTime.ToString("O"),
                ["Timestamp"] = DateTime.UtcNow.ToString("O")
            });

            if (result.ErrorCount > 0)
            {
                _logger.LogWarning("{JobName} completed with {ErrorCount} errors: {ErrorMessages}",
                    Metadata.JobName, result.ErrorCount, string.Join("; ", result.ErrorMessages));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed at {Timestamp}", Metadata.JobName, DateTime.UtcNow);
            _telemetryService.TrackException(ex, new Dictionary<string, string>
            {
                ["Job"] = Metadata.JobName,
                ["Timestamp"] = DateTime.UtcNow.ToString("O")
            });
            throw;
        }
    }
}
