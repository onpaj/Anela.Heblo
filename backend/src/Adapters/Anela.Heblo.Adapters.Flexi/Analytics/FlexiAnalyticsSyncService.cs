using Anela.Heblo.Persistence.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public record FlexiAnalyticsSyncReport(
    int TotalFetched,
    int TotalUpserted,
    int FailedServices,
    bool IsFullSuccess);

public sealed class FlexiAnalyticsSyncService : IFlexiAnalyticsSyncService
{
    /// <summary>
    /// The month-grain read models Metabase queries. They are materialized, so a Metabase card
    /// costs a few thousand pre-aggregated rows instead of a full double scan of ledger_entry
    /// (18.6 s measured) — at the price of needing a refresh, which belongs here rather than on a
    /// schedule of its own: stale-but-fast is a worse failure than slow, and it is silent.
    /// </summary>
    private static readonly string[] ReadModels =
    [
        "v_cost_monthly_total",
        "v_cost_monthly_by_account",
        "v_marketing_spend_monthly",
        "v_ad_spend_monthly",
    ];

    private readonly IEnumerable<IEntitySyncService> _services;
    private readonly AnalyticsDbContext _dbContext;
    private readonly ILogger<FlexiAnalyticsSyncService> _logger;

    public FlexiAnalyticsSyncService(
        IEnumerable<IEntitySyncService> services,
        AnalyticsDbContext dbContext,
        ILogger<FlexiAnalyticsSyncService> logger)
    {
        _services = services;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<FlexiAnalyticsSyncReport> SyncAllAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("FlexiAnalyticsSync.Started");

        var totalFetched = 0;
        var totalUpserted = 0;
        var failedServices = 0;

        foreach (var service in _services)
        {
            var serviceName = service.GetType().Name;

            // All four entity syncs share one scoped AnalyticsDbContext. On 2026-09-24 the ledger
            // sync left two failed INSERTs on the change tracker and every later entity then died
            // flushing them, so one bad ledger page was reported as a four-service outage. Start
            // each entity from a clean tracker so nobody inherits the last one's mess.
            _dbContext.ChangeTracker.Clear();

            try
            {
                var result = await service.SyncAsync(ct);

                _logger.LogInformation(
                    "FlexiAnalyticsSync.ServiceCompleted {ServiceName} rowsFetched={RowsFetched} rowsUpserted={RowsUpserted} isSuccess={IsSuccess}",
                    serviceName, result.RowsFetched, result.RowsUpserted, result.IsSuccess);

                if (result.IsSuccess)
                {
                    totalFetched += result.RowsFetched;
                    totalUpserted += result.RowsUpserted;
                }
                else
                {
                    failedServices++;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Carrying on round the loop with an already-cancelled token would just fail every
                // remaining service and report the shutdown as a multi-service outage.
                _logger.LogWarning("FlexiAnalyticsSync.Cancelled {ServiceName}", serviceName);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "FlexiAnalyticsSync.ServiceFailed {ServiceName}",
                    serviceName);

                failedServices++;
            }
        }

        // Only when every entity landed: refreshing off a half-synced ledger would publish a month
        // that is missing rows, which is worse than publishing yesterday's complete numbers.
        if (failedServices == 0)
        {
            await RefreshReadModelsAsync(ct);
        }
        else
        {
            _logger.LogWarning(
                "FlexiAnalyticsSync.ReadModelsNotRefreshed failedServices={FailedServices} — Metabase keeps the previous snapshot",
                failedServices);
        }

        _logger.LogInformation(
            "FlexiAnalyticsSync.Completed totalFetched={TotalFetched} totalUpserted={TotalUpserted} failedServices={FailedServices}",
            totalFetched, totalUpserted, failedServices);

        return new FlexiAnalyticsSyncReport(
            TotalFetched: totalFetched,
            TotalUpserted: totalUpserted,
            FailedServices: failedServices,
            IsFullSuccess: failedServices == 0);
    }

    /// <summary>
    /// Rebuilds the materialized read models Metabase reads. A failure here is logged but does not
    /// fail the sync: the rows are already in flexi_raw, and reporting a successful ingest as a
    /// failed one would mask the state of the thing that actually matters.
    /// </summary>
    private async Task RefreshReadModelsAsync(CancellationToken ct)
    {
        foreach (var readModel in ReadModels)
        {
            try
            {
                // Identifiers are compile-time constants from ReadModels, never user input, so
                // there is nothing to parameterise here — ExecuteSqlRaw cannot parameterise an
                // object name in any case.
                await _dbContext.Database.ExecuteSqlRawAsync(
                    $"REFRESH MATERIALIZED VIEW {AnalyticsDbContext.Schema}.{readModel};", ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "FlexiAnalyticsSync.ReadModelRefreshFailed {ReadModel} — Metabase will serve the previous snapshot",
                    readModel);
            }
        }

        _logger.LogInformation("FlexiAnalyticsSync.ReadModelsRefreshed count={Count}", ReadModels.Length);
    }
}
