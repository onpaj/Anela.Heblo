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
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _storeTimeZone;
    private readonly ILogger<ShoptetOrderIngestor> _logger;

    public ShoptetOrderIngestor(
        IShoptetOrderAnalyticsClient client,
        IShoptetOrderStore store,
        IOptions<ShoptetOrdersSyncOptions> options,
        TimeProvider timeProvider,
        ILogger<ShoptetOrderIngestor> logger)
    {
        _client = client;
        _store = store;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _storeTimeZone = ShoptetTimeZone.Resolve(_options.StoreTimeZone, logger);
    }

    public async Task<ShoptetIngestResult> IngestAsync(IReadOnlyList<string> codes, CancellationToken ct)
    {
        var fetched = 0;
        var upserted = 0;
        var missing = 0;
        var mismatched = 0;
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

            // The primary key is taken from the response body, so a body that answers about a
            // different order (or carries no code at all) would silently store a row under the
            // wrong key — and every code-less order would overwrite the previous one — while the
            // requested order simply went missing. Untrusted external JSON: check it.
            if (!string.Equals(dto.Code, code, StringComparison.Ordinal))
            {
                mismatched++;
                _logger.LogWarning(
                    "ShoptetOrdersSync.OrderCodeMismatch requested={Requested} returned={Returned}",
                    code, string.IsNullOrEmpty(dto.Code) ? "(empty)" : dto.Code);
                continue;
            }

            fetched++;
            batch.Add(ShoptetOrderMapper.Map(dto, rawJson, _storeTimeZone, _timeProvider.GetUtcNow()));

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

        if (mismatched > 0)
            _logger.LogWarning("ShoptetOrdersSync.MismatchedOrders count={Mismatched}", mismatched);

        return new ShoptetIngestResult(fetched, upserted, missing);
    }
}

public record ShoptetIngestResult(int RowsFetched, int RowsUpserted, int RowsMissing);
