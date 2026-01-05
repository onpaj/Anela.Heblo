using Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Xcc.Telemetry;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.Infrastructure.Jobs;

public class PurchasePriceRecalculationJob : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<PurchasePriceRecalculationJob> _logger;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ITelemetryService _telemetryService;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "purchase-price-recalculation",
        DisplayName = "Purchase Price Recalculation",
        Description = "Recalculates purchase prices for all materials and products",
        CronExpression = "0 2 * * *", // Daily at 2:00 AM
        DefaultIsEnabled = true
    };

    public PurchasePriceRecalculationJob(
        IMediator mediator,
        ILogger<PurchasePriceRecalculationJob> logger,
        IRecurringJobStatusChecker statusChecker,
        ITelemetryService telemetryService)
    {
        _mediator = mediator;
        _logger = logger;
        _statusChecker = statusChecker;
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

            var request = new RecalculatePurchasePriceRequest
            {
                RecalculateAll = true
            };

            var response = await _mediator.Send(request, cancellationToken);

            _logger.LogInformation("{JobName} completed - Success: {SuccessCount}, Failed: {FailedCount}, Total: {TotalCount}",
                Metadata.JobName, response.SuccessCount, response.FailedCount, response.TotalCount);

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
