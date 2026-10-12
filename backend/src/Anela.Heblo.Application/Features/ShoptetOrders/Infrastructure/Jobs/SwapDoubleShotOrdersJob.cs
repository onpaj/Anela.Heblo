using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.Jobs;

public sealed class SwapDoubleShotOrdersJob : IRecurringJob
{
    private readonly IDoubleShotOrderSwapService _swapService;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly IFeatureFlagChecker _featureFlags;
    private readonly ILogger<SwapDoubleShotOrdersJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "swap-double-shot-orders",
        Category = RecurringJobCategory.Warehouse,
        DisplayName = "Swap Double-Shot Orders",
        Description = "Replaces double-shot V variant lines in open Shoptet orders with their S mirror product set, so components are deducted, picked and invoiced.",
        CronExpression = "*/5 * * * *",
        DefaultIsEnabled = true,
    };

    public SwapDoubleShotOrdersJob(
        IDoubleShotOrderSwapService swapService,
        IRecurringJobStatusChecker statusChecker,
        IFeatureFlagChecker featureFlags,
        ILogger<SwapDoubleShotOrdersJob> logger)
    {
        _swapService = swapService;
        _statusChecker = statusChecker;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    // The 5-minute cron is the retry: Hangfire's default 10 retries would pile up on the single worker.
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var applyChanges = await _featureFlags.IsEnabledAsync(FeatureFlagKeys.DoubleShotOrderSwap, cancellationToken);
        if (!applyChanges)
        {
            _logger.LogInformation(
                "SwapDoubleShotOrders: running in DRY RUN mode (feature flag '{Flag}' disabled) — swaps are logged, not applied.",
                FeatureFlagKeys.DoubleShotOrderSwap);
        }

        await _swapService.SweepAsync(applyChanges, cancellationToken);
    }
}
