using Anela.Heblo.Adapters.ShoptetApi.Analytics.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

/// <summary>
/// Nightly catch-up. Orders mutate long after creation — status changes, payment, cancellation —
/// so this re-reads every order changed since the watermark and upserts it, replacing its lines.
///
/// It reads both GET /api/orders/changes and GET /api/orders?changeTimeFrom. The changes log is
/// the only source of deletions — a deleted order simply stops appearing in the order list, which
/// would leave it mirrored for ever — but its documented changeType is only "edit" or "delete", so
/// the change-time listing is unioned in to be sure newly created orders are picked up too. The log
/// keeps a guaranteed 30 days, so when the watermark is older than that the run falls back to the
/// listing alone and reports that deletions in that window were not covered.
/// </summary>
public sealed class ShoptetOrderIncrementalSyncService : IShoptetEntitySyncService
{
    public const string EntityNameConst = "order";

    /// <summary>Shoptet guarantees 30 days of change log; stay a day inside it.</summary>
    private const int ChangeLogGuaranteedDays = 29;

    private const int ChangesPageSize = 1000;

    private readonly IShoptetOrderAnalyticsClient _client;
    private readonly ShoptetOrderIngestor _ingestor;
    private readonly IShoptetOrderStore _store;
    private readonly IShoptetSyncWatermarkRepository _watermarkRepo;
    private readonly ShoptetOrdersSyncOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ShoptetOrderIncrementalSyncService> _logger;

    public string EntityName => EntityNameConst;

    public ShoptetOrderIncrementalSyncService(
        IShoptetOrderAnalyticsClient client,
        ShoptetOrderIngestor ingestor,
        IShoptetOrderStore store,
        IShoptetSyncWatermarkRepository watermarkRepo,
        IOptions<ShoptetOrdersSyncOptions> options,
        TimeProvider timeProvider,
        ILogger<ShoptetOrderIncrementalSyncService> logger)
    {
        _client = client;
        _ingestor = ingestor;
        _store = store;
        _watermarkRepo = watermarkRepo;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ShoptetSyncResult> SyncAsync(CancellationToken ct = default)
    {
        var state = await _watermarkRepo.GetOrCreateAsync(EntityName, ct);
        var runStartedAt = _timeProvider.GetUtcNow();

        // Watermark minus a safety margin, the same shape as LedgerSyncService's AddHours(-1).
        var changedSince = state.Watermark.HasValue
            ? state.Watermark.Value.AddHours(-_options.WatermarkSafetyMarginHours)
            : await ColdStartFromAsync(runStartedAt, ct);

        state.LastRunStartedAt = runStartedAt;
        state.LastRunStatus = ShoptetSyncStatus.Running;
        await _watermarkRepo.SaveAsync(state, ct);

        _logger.LogInformation(
            "ShoptetOrdersSync.IncrementalStarted watermark={Watermark} changedSince={ChangedSince}",
            state.Watermark, changedSince);

        var totalFetched = 0;
        var totalUpserted = 0;

        try
        {
            var withinChangeLog = changedSince >= runStartedAt.AddDays(-ChangeLogGuaranteedDays);

            List<string> editedCodes;
            var deletedCodes = new List<string>();

            if (withinChangeLog)
            {
                (editedCodes, deletedCodes) = await ReadChangeLogAsync(changedSince, ct);

                // The change log is the only source of deletions, but its documented changeType is
                // "edit" or "delete" — creation is never mentioned, and this store has not been
                // observed emitting one. Relying on it alone would silently stop ingesting new
                // orders the day the backfill completes, so the change-time listing (where a new
                // order appears because its changeTime equals its creationTime) is unioned in.
                // One extra paged listing per night is cheap next to that failure mode.
                var changedCodes = await ReadChangedOrderCodesAsync(changedSince, ct);
                var union = new HashSet<string>(editedCodes, StringComparer.Ordinal);
                union.UnionWith(changedCodes);
                union.ExceptWith(deletedCodes);
                editedCodes = union.ToList();
            }
            else
            {
                _logger.LogWarning(
                    "ShoptetOrdersSync.ChangeLogWindowExceeded changedSince={ChangedSince} — falling back "
                    + "to the order list; deletions in this window will not be detected",
                    changedSince);
                editedCodes = await ReadChangedOrderCodesAsync(changedSince, ct);
            }

            var deleted = await _store.DeleteAsync(deletedCodes, ct);

            var result = await _ingestor.IngestAsync(editedCodes, ct);
            totalFetched = result.RowsFetched;
            totalUpserted = result.RowsUpserted;

            state.Watermark = runStartedAt;
            state.LastRunStatus = ShoptetSyncStatus.Ok;
            state.LastRunFinishedAt = _timeProvider.GetUtcNow();
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalUpserted;
            state.LastErrorMessage = null;

            _logger.LogInformation(
                "ShoptetOrdersSync.IncrementalCompleted edited={Edited} upserted={Upserted} deleted={Deleted}",
                editedCodes.Count, totalUpserted, deleted);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            state.LastRunStatus = ShoptetSyncStatus.Failed;
            state.LastRunFinishedAt = _timeProvider.GetUtcNow();
            state.LastErrorMessage = ShoptetSyncStatus.TruncateError(ex.Message);
            _logger.LogError(ex, "ShoptetOrdersSync.IncrementalFailed");
        }
        catch (OperationCanceledException)
        {
            // Same reasoning as the backfill: a cancelled run still has to leave a terminal status
            // behind, or v_sync_health shows a run that never finished.
            state.LastRunStatus = ShoptetSyncStatus.Cancelled;
            state.LastRunFinishedAt = _timeProvider.GetUtcNow();
            _logger.LogWarning("ShoptetOrdersSync.IncrementalCancelled");
        }

        await _watermarkRepo.SaveAsync(state, CancellationToken.None);
        return new ShoptetSyncResult(
            totalFetched, totalUpserted, state.LastRunStatus == ShoptetSyncStatus.Ok, IsComplete: true);
    }

    /// <summary>
    /// Where to start when no watermark exists yet. The backfill leaves orders mirrored as they
    /// looked when its window passed them, so the first incremental run has to reach back to when
    /// the backfill ran — not merely one change-log window, which would silently skip every edit in
    /// between if the two are far apart. They can be: the backfill is run by hand, and the nightly
    /// job stays unregistered until the connection-string secret is created.
    ///
    /// Reaching further back than the guaranteed 30 days deliberately drops the run into the
    /// change-time fallback, which catches every edit in the gap and says out loud that deletions
    /// in it were not covered.
    /// </summary>
    private async Task<DateTimeOffset> ColdStartFromAsync(DateTimeOffset runStartedAt, CancellationToken ct)
    {
        var oneWindowBack = runStartedAt.AddDays(-ChangeLogGuaranteedDays);

        var backfillState = await _watermarkRepo.GetOrCreateAsync(
            ShoptetOrderBackfillService.EntityNameConst, ct);

        var backfillRanAt = backfillState.LastRunStartedAt;
        if (backfillRanAt == null || backfillRanAt >= oneWindowBack)
            return oneWindowBack;

        _logger.LogWarning(
            "ShoptetOrdersSync.ColdStartAfterOldBackfill backfillRanAt={BackfillRanAt} — reaching "
            + "back to the backfill rather than one change-log window, because everything edited "
            + "since then is still mirrored as the backfill left it",
            backfillRanAt);

        return backfillRanAt.Value;
    }

    private async Task<(List<string> Edited, List<string> Deleted)> ReadChangeLogAsync(
        DateTimeOffset changedSince, CancellationToken ct)
    {
        var edited = new HashSet<string>(StringComparer.Ordinal);
        var deleted = new HashSet<string>(StringComparer.Ordinal);
        var page = 1;
        var read = 0;
        ShoptetPaginatorDto? paginator;
        const string description = "order changes";

        while (true)
        {
            var data = await _client.ListChangesAsync(changedSince, page, ChangesPageSize, ct);

            foreach (var change in data.Changes)
            {
                if (string.Equals(change.ChangeType, "delete", StringComparison.OrdinalIgnoreCase))
                    deleted.Add(change.Code);
                else
                    edited.Add(change.Code);
            }

            read += data.Changes.Count;
            paginator = data.Paginator;
            if (ShoptetPaging.IsLastPage(paginator, data.Changes.Count, page, description))
                break;

            page++;
        }

        ShoptetPaging.EnsureComplete(paginator, read, description);

        // The log records only an order's last change, so a code never appears as both.
        edited.ExceptWith(deleted);
        return (edited.ToList(), deleted.ToList());
    }

    private async Task<List<string>> ReadChangedOrderCodesAsync(
        DateTimeOffset changedSince, CancellationToken ct)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var page = 1;
        var read = 0;
        ShoptetPaginatorDto? paginator;
        const string description = "orders by change time";

        while (true)
        {
            var data = await _client.ListCodesByChangeTimeAsync(changedSince, page, ct);
            foreach (var order in data.Orders)
                codes.Add(order.Code);

            read += data.Orders.Count;
            paginator = data.Paginator;
            if (ShoptetPaging.IsLastPage(paginator, data.Orders.Count, page, description))
                break;

            page++;
        }

        ShoptetPaging.EnsureComplete(paginator, read, description);
        return codes.ToList();
    }
}
