using Anela.Heblo.Application.Features.Catalog.DoubleShots;
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs;

/// <summary>
/// Keeps each double-shot variant's (SA…V) Shoptet stock equal to its mirror product set's (SA…S)
/// computed stock. Dry run unless <see cref="FeatureFlagKeys.DoubleShotAvailabilitySync"/> is on.
/// Writes are relative, so each variant's live stock is read right before its write: a cached CSV,
/// a manual re-run or an earlier duplicated PATCH can then never compound.
/// See docs/processes/job-doubleshot-availability-sync.md.
/// </summary>
public sealed class DoubleShotAvailabilitySyncJob : IRecurringJob
{
    private const int LockTimeoutSeconds = 600;

    private readonly IEshopStockClient _stockClient;
    private readonly ICatalogResilienceService _resilience;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly IFeatureFlagChecker _featureFlags;
    private readonly ILogger<DoubleShotAvailabilitySyncJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "doubleshot-availability-sync",
        Category = RecurringJobCategory.Catalog,
        DisplayName = "Double-shot Availability Sync",
        Description = "Sets each double-shot variant's (SA…V) Shoptet stock to its mirror product set's (SA…S) stock.",
        CronExpression = "*/15 * * * *",
        DefaultIsEnabled = true,
    };

    public DoubleShotAvailabilitySyncJob(
        IEshopStockClient stockClient,
        ICatalogResilienceService resilience,
        IRecurringJobStatusChecker statusChecker,
        IFeatureFlagChecker featureFlags,
        ILogger<DoubleShotAvailabilitySyncJob> logger)
    {
        _stockClient = stockClient;
        _resilience = resilience;
        _statusChecker = statusChecker;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    [DisableConcurrentExecution(LockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var applyChanges = await _featureFlags.IsEnabledAsync(
            FeatureFlagKeys.DoubleShotAvailabilitySync, cancellationToken);

        var stock = await TryListStockAsync(cancellationToken);
        if (stock is null)
            return;

        var plan = DoubleShotStockPlanner.Plan(stock);
        foreach (var code in plan.UnpairedVariantCodes)
        {
            DoubleShotCodeRule.TryGetSetCode(code, out var setCode);
            _logger.LogWarning(
                "DoubleShotAvailabilitySync: variant {VariantCode} has no mirror set {SetCode} in the stock export. Skipped.",
                code, setCode);
        }

        if (!applyChanges)
        {
            LogDryRun(plan);
            return;
        }

        var (changed, failed) = await ApplyAsync(plan.Pairs, cancellationToken);

        _logger.LogInformation(
            "DoubleShotAvailabilitySync: {Pairs} pair(s), {Changed} changed, {Failed} failed, {Unpaired} unpaired.",
            plan.PairCount, changed, failed, plan.UnpairedVariantCodes.Count);
    }

    private async Task<List<EshopStock>?> TryListStockAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _resilience.ExecuteWithResilienceAsync(
                ct => _stockClient.ListAsync(ct), "DoubleShotAvailabilitySync.ListStock", cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "DoubleShotAvailabilitySync: failed to read the Shoptet stock export. No changes made.");
            return null;
        }
    }

    private void LogDryRun(DoubleShotStockPlan plan)
    {
        foreach (var a in plan.Adjustments)
        {
            _logger.LogInformation(
                "DoubleShotAvailabilitySync [DRY RUN]: {VariantCode} {Old} -> {New} (delta {Delta}, mirror {SetCode}). No changes made.",
                a.VariantCode, a.Current, a.Target, a.Delta, a.SetCode);
        }

        _logger.LogInformation(
            "DoubleShotAvailabilitySync: {Pairs} pair(s), {Planned} to change, {Unpaired} unpaired [DRY RUN — no changes applied].",
            plan.PairCount, plan.Adjustments.Count, plan.UnpairedVariantCodes.Count);
    }

    private async Task<(int Changed, int Failed)> ApplyAsync(
        IReadOnlyList<DoubleShotStockAdjustment> pairs, CancellationToken cancellationToken)
    {
        var changed = 0;
        var failed = 0;

        foreach (var pair in pairs)
        {
            try
            {
                if (await SyncVariantAsync(pair, cancellationToken))
                    changed++;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                _logger.LogWarning(ex,
                    "DoubleShotAvailabilitySync: failed to update {VariantCode} to {New}. Will retry next run.",
                    pair.VariantCode, pair.Target);
            }
        }

        return (changed, failed);
    }

    /// <returns><c>true</c> when a movement was written.</returns>
    private async Task<bool> SyncVariantAsync(DoubleShotStockAdjustment pair, CancellationToken cancellationToken)
    {
        var supply = await _stockClient.GetSupplyAsync(pair.VariantCode, cancellationToken)
            ?? throw new InvalidOperationException($"Shoptet returned no supply for {pair.VariantCode}.");

        var live = (decimal)supply.Amount;
        var delta = pair.Target - live;
        if (delta == 0m)
            return false;

        await _stockClient.UpdateStockAsync(pair.VariantCode, (double)delta, cancellationToken);
        _logger.LogInformation(
            "DoubleShotAvailabilitySync: {VariantCode} {Old} -> {New} (delta {Delta}, mirror {SetCode}).",
            pair.VariantCode, live, pair.Target, delta, pair.SetCode);
        return true;
    }
}
