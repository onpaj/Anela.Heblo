using Anela.Heblo.Application.Features.Catalog.DoubleShots;
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs;

/// <summary>
/// Keeps each double-shot variant's (SA…V) Shoptet stock equal to its mirror product set's (SA…S)
/// computed stock. Dry run unless <see cref="FeatureFlagKeys.DoubleShotAvailabilitySync"/> is on.
/// See docs/processes/job-doubleshot-availability-sync.md.
/// </summary>
public sealed class DoubleShotAvailabilitySyncJob : IRecurringJob
{
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

        var (changed, failed) = await ApplyAsync(plan.Adjustments, applyChanges, cancellationToken);

        _logger.LogInformation(
            "DoubleShotAvailabilitySync: {Pairs} pair(s), {Planned} to change, {Changed} changed, {Failed} failed, {Unpaired} unpaired{Mode}.",
            plan.PairCount, plan.Adjustments.Count, changed, failed, plan.UnpairedVariantCodes.Count,
            applyChanges ? string.Empty : " [DRY RUN — no changes applied]");
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

    private async Task<(int Changed, int Failed)> ApplyAsync(
        IReadOnlyList<DoubleShotStockAdjustment> adjustments, bool applyChanges, CancellationToken cancellationToken)
    {
        var changed = 0;
        var failed = 0;

        foreach (var a in adjustments)
        {
            if (!applyChanges)
            {
                _logger.LogInformation(
                    "DoubleShotAvailabilitySync [DRY RUN]: {VariantCode} {Old} -> {New} (delta {Delta}, mirror {SetCode}). No changes made.",
                    a.VariantCode, a.Current, a.Target, a.Delta, a.SetCode);
                continue;
            }

            try
            {
                await _stockClient.UpdateStockAsync(a.VariantCode, (double)a.Delta, cancellationToken);
                changed++;
                _logger.LogInformation(
                    "DoubleShotAvailabilitySync: {VariantCode} {Old} -> {New} (delta {Delta}, mirror {SetCode}).",
                    a.VariantCode, a.Current, a.Target, a.Delta, a.SetCode);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                _logger.LogWarning(ex,
                    "DoubleShotAvailabilitySync: failed to update {VariantCode} ({Old} -> {New}). Will retry next run.",
                    a.VariantCode, a.Current, a.Target);
            }
        }

        return (changed, failed);
    }
}
