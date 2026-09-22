using Anela.Heblo.Persistence.ShoptetOrders.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

/// <summary>
/// Turns a stream of order codes into shoptet_raw rows: one detail call per code (the list
/// endpoint carries no items[]), mapped and written in batches. Shared by the backfill and the
/// nightly incremental sync so both apply exactly the same mapping and batching rules.
/// </summary>
public sealed class ShoptetOrderIngestor
{
    private readonly IShoptetOrderAnalyticsClient _client;
    private readonly IShoptetOrderStore _store;
    private readonly ShoptetOrdersSyncOptions _options;
    private readonly TimeZoneInfo _storeTimeZone;
    private readonly ILogger<ShoptetOrderIngestor> _logger;

    public ShoptetOrderIngestor(
        IShoptetOrderAnalyticsClient client,
        IShoptetOrderStore store,
        IOptions<ShoptetOrdersSyncOptions> options,
        ILogger<ShoptetOrderIngestor> logger)
    {
        _client = client;
        _store = store;
        _options = options.Value;
        _logger = logger;
        _storeTimeZone = ResolveStoreTimeZone(_options.StoreTimeZone, logger);
    }

    public async Task<ShoptetIngestResult> IngestAsync(IReadOnlyList<string> codes, CancellationToken ct)
    {
        var fetched = 0;
        var upserted = 0;
        var missing = 0;
        var batch = new List<ShoptetOrder>(_options.BatchSize);

        foreach (var code in codes)
        {
            ct.ThrowIfCancellationRequested();

            var (dto, rawJson) = await _client.GetOrderWithRawAsync(code, ct);
            if (dto == null)
            {
                // Deleted between the listing and the detail call — normal on a multi-hour backfill.
                missing++;
                continue;
            }

            fetched++;
            batch.Add(ShoptetOrderMapper.Map(dto, rawJson, _storeTimeZone, DateTimeOffset.UtcNow));

            if (batch.Count >= _options.BatchSize)
            {
                upserted += await _store.UpsertAsync(batch, ct);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
            upserted += await _store.UpsertAsync(batch, ct);

        if (missing > 0)
            _logger.LogInformation("ShoptetOrdersSync.MissingOrders count={Missing}", missing);

        return new ShoptetIngestResult(fetched, upserted, missing);
    }

    private static TimeZoneInfo ResolveStoreTimeZone(string id, ILogger logger)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning(ex,
                "ShoptetOrdersSync.UnknownStoreTimeZone id={TimeZoneId} — falling back to UTC", id);
            return TimeZoneInfo.Utc;
        }
    }
}

public record ShoptetIngestResult(int RowsFetched, int RowsUpserted, int RowsMissing);
