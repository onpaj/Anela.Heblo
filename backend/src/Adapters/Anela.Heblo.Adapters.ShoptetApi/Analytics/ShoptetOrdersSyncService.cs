using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

/// <summary>
/// Orchestrates the two phases of the shoptet_raw mirror. While the full history is still being
/// backfilled the run spends its whole budget there; once the backfill reports complete, every run
/// is the nightly incremental catch-up. Both phases are idempotent, so a run that is cut short
/// simply resumes on the next one.
/// </summary>
public sealed class ShoptetOrdersSyncService : IShoptetOrdersSyncService
{
    private readonly ShoptetOrderBackfillService _backfill;
    private readonly ShoptetOrderIncrementalSyncService _incremental;
    private readonly ILogger<ShoptetOrdersSyncService> _logger;

    public ShoptetOrdersSyncService(
        ShoptetOrderBackfillService backfill,
        ShoptetOrderIncrementalSyncService incremental,
        ILogger<ShoptetOrdersSyncService> logger)
    {
        _backfill = backfill;
        _incremental = incremental;
        _logger = logger;
    }

    public async Task<ShoptetOrdersSyncReport> SyncAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("ShoptetOrdersSync.Started");

        var backfillResult = await _backfill.SyncAsync(ct);

        if (!backfillResult.IsComplete)
        {
            _logger.LogInformation(
                "ShoptetOrdersSync.BackfillInProgress fetched={Fetched} upserted={Upserted} success={Success}",
                backfillResult.RowsFetched, backfillResult.RowsUpserted, backfillResult.IsSuccess);

            return new ShoptetOrdersSyncReport(
                backfillResult.RowsFetched,
                backfillResult.RowsUpserted,
                BackfillCompleted: false,
                IsFullSuccess: backfillResult.IsSuccess);
        }

        var incrementalResult = await _incremental.SyncAsync(ct);

        _logger.LogInformation(
            "ShoptetOrdersSync.Completed fetched={Fetched} upserted={Upserted} success={Success}",
            incrementalResult.RowsFetched, incrementalResult.RowsUpserted, incrementalResult.IsSuccess);

        return new ShoptetOrdersSyncReport(
            backfillResult.RowsFetched + incrementalResult.RowsFetched,
            backfillResult.RowsUpserted + incrementalResult.RowsUpserted,
            BackfillCompleted: true,
            IsFullSuccess: backfillResult.IsSuccess && incrementalResult.IsSuccess);
    }
}
