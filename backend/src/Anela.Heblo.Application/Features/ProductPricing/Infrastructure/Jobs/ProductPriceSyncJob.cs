using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ProductPricing.Infrastructure.Jobs;

public class ProductPriceSyncJob : IRecurringJob
{
    private readonly IProductPriceSyncService _syncService;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<ProductPriceSyncJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "product-price-sync",
        DisplayName = "Product Price Sync",
        Description = "Pushes Heblo retail prices to Shoptet and Flexi and detects downstream drift",
        CronExpression = "0 * * * *", // Hourly — a price edit reaches both systems within the hour
        DefaultIsEnabled = true,
    };

    public ProductPriceSyncJob(
        IProductPriceSyncService syncService,
        IRecurringJobStatusChecker statusChecker,
        ILogger<ProductPriceSyncJob> logger)
    {
        _syncService = syncService;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        _logger.LogInformation("Starting {JobName}", Metadata.JobName);

        try
        {
            var result = await _syncService.SyncAsync(cancellationToken);

            _logger.LogInformation(
                "{JobName} completed: {Pushed} pushed, {Conflicts} conflicts, {Failed} failed",
                Metadata.JobName, result.Pushed, result.Conflicts, result.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw; // Re-throw to let Hangfire handle retry logic
        }
    }
}
