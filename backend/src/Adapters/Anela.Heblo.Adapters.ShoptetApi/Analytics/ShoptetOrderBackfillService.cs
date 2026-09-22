using Anela.Heblo.Persistence.ShoptetOrders.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

/// <summary>
/// Walks the full order history forward in creation-time windows, persisting the cursor after each
/// window so an interrupted run resumes where it stopped.
///
/// Creation time, not change time, is what makes this resumable: an order's creationTime never
/// moves, so a window's contents and its paging are stable. (Paging the unfiltered list is not —
/// Shoptet's default order is not creation-time descending, so page 1932 of 1932 is not the oldest
/// order.) Re-running a window re-upserts the same order codes, which is why the whole operation is
/// idempotent.
///
/// GET /api/orders/snapshot — the documented full export — cannot be used: it answers 403
/// "Webhook for job:finished is not registered", and this store has no webhooks registered.
/// </summary>
public sealed class ShoptetOrderBackfillService : IShoptetEntitySyncService
{
    public const string EntityNameConst = "order_backfill";

    private readonly IShoptetOrderAnalyticsClient _client;
    private readonly ShoptetOrderIngestor _ingestor;
    private readonly IShoptetSyncWatermarkRepository _watermarkRepo;
    private readonly ShoptetOrdersSyncOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ShoptetOrderBackfillService> _logger;

    public string EntityName => EntityNameConst;

    public ShoptetOrderBackfillService(
        IShoptetOrderAnalyticsClient client,
        ShoptetOrderIngestor ingestor,
        IShoptetSyncWatermarkRepository watermarkRepo,
        IOptions<ShoptetOrdersSyncOptions> options,
        TimeProvider timeProvider,
        ILogger<ShoptetOrderBackfillService> logger)
    {
        _client = client;
        _ingestor = ingestor;
        _watermarkRepo = watermarkRepo;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ShoptetSyncResult> SyncAsync(CancellationToken ct = default)
    {
        var state = await _watermarkRepo.GetOrCreateAsync(EntityName, ct);

        if (state.BackfillCompleted)
            return new ShoptetSyncResult(0, 0, IsSuccess: true, IsComplete: true);

        var cursor = state.BackfillCursor ?? _options.GetBackfillFromDate();
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime).AddDays(1);
        var deadline = _timeProvider.GetUtcNow().AddMinutes(_options.BackfillMaxMinutesPerRun);

        state.LastRunStartedAt = _timeProvider.GetUtcNow();
        state.LastRunStatus = "RUNNING";
        await _watermarkRepo.SaveAsync(state, ct);

        var totalFetched = 0;
        var totalUpserted = 0;
        var completed = false;

        try
        {
            while (cursor < today)
            {
                ct.ThrowIfCancellationRequested();

                if (_timeProvider.GetUtcNow() >= deadline)
                {
                    _logger.LogInformation(
                        "ShoptetOrdersSync.BackfillBudgetExhausted cursor={Cursor}", cursor);
                    break;
                }

                var windowEnd = MinDate(cursor.AddDays(_options.BackfillWindowDays), today);
                var codes = await ListWindowCodesAsync(cursor, windowEnd, ct);

                var result = await _ingestor.IngestAsync(codes, ct);
                totalFetched += result.RowsFetched;
                totalUpserted += result.RowsUpserted;

                _logger.LogInformation(
                    "ShoptetOrdersSync.BackfillWindow from={From} to={To} codes={Codes} upserted={Upserted}",
                    cursor, windowEnd, codes.Count, result.RowsUpserted);

                cursor = windowEnd;
                state.BackfillCursor = cursor;
                await _watermarkRepo.SaveAsync(state, ct);
            }

            completed = cursor >= today;

            state.BackfillCompleted = completed;
            state.LastRunStatus = "OK";
            state.LastRunFinishedAt = _timeProvider.GetUtcNow();
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalUpserted;
            state.LastErrorMessage = null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            state.LastRunStatus = "FAILED";
            state.LastRunFinishedAt = _timeProvider.GetUtcNow();
            state.LastErrorMessage = Truncate(ex.Message);
            _logger.LogError(ex, "ShoptetOrdersSync.BackfillFailed cursor={Cursor}", cursor);
        }

        await _watermarkRepo.SaveAsync(state, ct);
        return new ShoptetSyncResult(totalFetched, totalUpserted, state.LastRunStatus == "OK", completed);
    }

    private async Task<List<string>> ListWindowCodesAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var fromOffset = ToStoreOffset(from);
        var toOffset = ToStoreOffset(to);

        var codes = new List<string>();
        var page = 1;

        while (true)
        {
            var data = await _client.ListCodesByCreationTimeAsync(fromOffset, toOffset, page, ct);
            codes.AddRange(data.Orders.Select(o => o.Code));

            var paginator = data.Paginator;
            if (paginator == null || page >= paginator.PageCount || data.Orders.Count == 0)
                break;

            page++;
        }

        // The window boundary is inclusive on both ends in Shoptet's filter, so an order created
        // exactly at midnight appears in two adjacent windows. Upserts make that harmless, but the
        // codes are de-duplicated anyway to avoid a wasted detail call.
        return codes.Distinct().ToList();
    }

    private DateTimeOffset ToStoreOffset(DateOnly date)
    {
        var storeZone = ResolveZone(_options.StoreTimeZone);
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, storeZone.GetUtcOffset(local));
    }

    private static TimeZoneInfo ResolveZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static DateOnly MinDate(DateOnly a, DateOnly b) => a < b ? a : b;

    private static string Truncate(string message) =>
        message.Length > 2000 ? message[..2000] : message;
}
