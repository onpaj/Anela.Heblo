using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

/// <summary>
/// Nightly catch-up. Orders mutate long after creation — status changes, payment, cancellation —
/// so this re-reads every order changed since the watermark and upserts it, replacing its lines.
///
/// It reads GET /api/orders/changes rather than GET /api/orders?changeTimeFrom, because only the
/// changes log reports deletions: a deleted order simply stops appearing in the order list, which
/// would leave it mirrored forever. The log keeps a guaranteed 30 days, so when the watermark is
/// older than that the run falls back to the order list and reports that deletions were not covered.
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
        // First run falls back to one guaranteed change-log window.
        var changedSince = state.Watermark.HasValue
            ? state.Watermark.Value.AddHours(-_options.WatermarkSafetyMarginHours)
            : runStartedAt.AddDays(-ChangeLogGuaranteedDays);

        state.LastRunStartedAt = runStartedAt;
        state.LastRunStatus = "RUNNING";
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
            state.LastRunStatus = "OK";
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
            state.LastRunStatus = "FAILED";
            state.LastRunFinishedAt = _timeProvider.GetUtcNow();
            state.LastErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            _logger.LogError(ex, "ShoptetOrdersSync.IncrementalFailed");
        }

        await _watermarkRepo.SaveAsync(state, ct);
        return new ShoptetSyncResult(
            totalFetched, totalUpserted, state.LastRunStatus == "OK", IsComplete: true);
    }

    private async Task<(List<string> Edited, List<string> Deleted)> ReadChangeLogAsync(
        DateTimeOffset changedSince, CancellationToken ct)
    {
        var edited = new HashSet<string>(StringComparer.Ordinal);
        var deleted = new HashSet<string>(StringComparer.Ordinal);
        var page = 1;

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

            var paginator = data.Paginator;
            if (paginator == null || page >= paginator.PageCount || data.Changes.Count == 0)
                break;

            page++;
        }

        // The log records only an order's last change, so a code never appears as both.
        edited.ExceptWith(deleted);
        return (edited.ToList(), deleted.ToList());
    }

    private async Task<List<string>> ReadChangedOrderCodesAsync(
        DateTimeOffset changedSince, CancellationToken ct)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var page = 1;

        while (true)
        {
            var data = await _client.ListCodesByChangeTimeAsync(changedSince, page, ct);
            foreach (var order in data.Orders)
                codes.Add(order.Code);

            var paginator = data.Paginator;
            if (paginator == null || page >= paginator.PageCount || data.Orders.Count == 0)
                break;

            page++;
        }

        return codes.ToList();
    }
}
