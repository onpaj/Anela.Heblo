using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

public sealed class CatalogCacheStore
{
    private const string CurrentCatalogCacheKey = "CatalogData_Current";
    private const string StaleCatalogCacheKey = "CatalogData_Stale";
    private const string CacheUpdateTimeKey = "CatalogData_LastUpdate";
    private const string LastMergeDateTimeKey = "LastMergeDateTime";

    public static class SourceKeys
    {
        public const string Sales = "CachedSalesData";
        public const string Attributes = "CachedCatalogAttributesData";
        public const string InTransport = "CachedInTransportData";
        public const string Manufactured = "CachedManufacturedData";
        public const string InReserve = "CachedInReserveData";
        public const string InQuarantine = "CachedInQuarantineData";
        public const string Ordered = "CachedOrderedData";
        public const string Planned = "CachedPlannedData";
        public const string ErpStock = "CachedErpStockData";
        public const string EshopStock = "CachedEshopStockData";
        public const string PurchaseHistory = "CachedPurchaseHistoryData";
        public const string ManufactureHistory = "CachedManufactureHistoryData";
        public const string Consumed = "CachedConsumedData";
        public const string StockTaking = "CachedStockTakingData";
        public const string Lots = "CachedLotsData";
        public const string EshopPrice = "CachedEshopPriceData";
        public const string ErpPrice = "CachedErpPriceData";
        public const string EshopUrl = "CachedEshopUrlData";
        public const string ManufactureDifficultySettings = "CachedManufactureDifficultySettingsData";
    }

    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<CatalogCacheOptions> _cacheOptions;
    private readonly ICatalogMergeScheduler _mergeScheduler;
    private readonly ILogger<CatalogCacheStore> _logger;
    private readonly SemaphoreSlim _cacheReplacementSemaphore = new(1, 1);

    public CatalogCacheStore(
        IMemoryCache cache,
        TimeProvider timeProvider,
        IOptions<CatalogCacheOptions> cacheOptions,
        ICatalogMergeScheduler mergeScheduler,
        ILogger<CatalogCacheStore> logger)
    {
        _cache = cache;
        _timeProvider = timeProvider;
        _cacheOptions = cacheOptions;
        _mergeScheduler = mergeScheduler;
        _logger = logger;
    }

    public async Task ReplaceCacheAtomicallyAsync(List<CatalogAggregate> newData)
    {
        await _cacheReplacementSemaphore.WaitAsync();
        try
        {
            var currentCache = _cache.Get<List<CatalogAggregate>>(CurrentCatalogCacheKey);
            if (currentCache != null)
            {
                var staleExpiry = _cacheOptions.Value.StaleDataRetentionPeriod;
                _cache.Set(StaleCatalogCacheKey, currentCache, staleExpiry);
            }
            _cache.Set(CurrentCatalogCacheKey, newData);
            _cache.Set(CacheUpdateTimeKey, DateTime.UtcNow);

            _logger.LogDebug("Cache updated atomically with {ProductCount} products", newData.Count);
        }
        finally
        {
            _cacheReplacementSemaphore.Release();
        }
    }

    public bool IsCacheValid()
    {
        var lastUpdate = _cache.Get<DateTime?>(CacheUpdateTimeKey);
        if (!lastUpdate.HasValue) return false;
        return DateTime.UtcNow - lastUpdate.Value < _cacheOptions.Value.CacheValidityPeriod;
    }

    public List<CatalogAggregate>? TryGetCurrent() =>
        _cache.Get<List<CatalogAggregate>>(CurrentCatalogCacheKey);

    public List<CatalogAggregate>? TryGetStale() =>
        _cache.Get<List<CatalogAggregate>>(StaleCatalogCacheKey);

    public List<CatalogAggregate> GetCatalogData()
    {
        if (_cache.TryGetValue(CurrentCatalogCacheKey, out List<CatalogAggregate>? current) && current != null)
            return current;

        if (_cache.TryGetValue(StaleCatalogCacheKey, out List<CatalogAggregate>? stale) && stale != null)
        {
            try { _mergeScheduler.ScheduleMerge("CacheRead"); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to schedule background merge when serving stale data"); }
            return stale;
        }

        _logger.LogWarning("No catalog data available in cache, triggering background merge");
        try { _mergeScheduler.ScheduleMerge("CacheEmpty"); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to schedule background merge for missing catalog data"); }

        return new List<CatalogAggregate>();
    }

    public IList<CatalogSaleRecord> GetSalesData() =>
        _cache.Get<List<CatalogSaleRecord>>(SourceKeys.Sales) ?? new List<CatalogSaleRecord>();
    public void SetSalesData(IList<CatalogSaleRecord> value) => Write(SourceKeys.Sales, value);

    public IList<CatalogAttributes> GetCatalogAttributesData() =>
        _cache.Get<List<CatalogAttributes>>(SourceKeys.Attributes) ?? new List<CatalogAttributes>();
    public void SetCatalogAttributesData(IList<CatalogAttributes> value) => Write(SourceKeys.Attributes, value);

    public IDictionary<string, int> GetInTransportData() =>
        _cache.Get<Dictionary<string, int>>(SourceKeys.InTransport) ?? new Dictionary<string, int>();
    public void SetInTransportData(IDictionary<string, int> value) => Write(SourceKeys.InTransport, value);

    public IDictionary<string, decimal> GetManufacturedData() =>
        _cache.Get<Dictionary<string, decimal>>(SourceKeys.Manufactured) ?? new Dictionary<string, decimal>();
    public void SetManufacturedData(IDictionary<string, decimal> value) => Write(SourceKeys.Manufactured, value);

    public IDictionary<string, int> GetInReserveData() =>
        _cache.Get<Dictionary<string, int>>(SourceKeys.InReserve) ?? new Dictionary<string, int>();
    public void SetInReserveData(IDictionary<string, int> value) => Write(SourceKeys.InReserve, value);

    public IDictionary<string, int> GetInQuarantineData() =>
        _cache.Get<Dictionary<string, int>>(SourceKeys.InQuarantine) ?? new Dictionary<string, int>();
    public void SetInQuarantineData(IDictionary<string, int> value) => Write(SourceKeys.InQuarantine, value);

    public IDictionary<string, decimal> GetOrderedData() =>
        _cache.Get<Dictionary<string, decimal>>(SourceKeys.Ordered) ?? new Dictionary<string, decimal>();
    public void SetOrderedData(IDictionary<string, decimal> value) => Write(SourceKeys.Ordered, value);

    public IDictionary<string, decimal> GetPlannedData() =>
        _cache.Get<Dictionary<string, decimal>>(SourceKeys.Planned) ?? new Dictionary<string, decimal>();
    public void SetPlannedData(IDictionary<string, decimal> value) => Write(SourceKeys.Planned, value);

    public IList<ErpStock> GetErpStockData() =>
        _cache.Get<List<ErpStock>>(SourceKeys.ErpStock) ?? new List<ErpStock>();
    public void SetErpStockData(IList<ErpStock> value) => Write(SourceKeys.ErpStock, value);

    public IList<EshopStock> GetEshopStockData() =>
        _cache.Get<List<EshopStock>>(SourceKeys.EshopStock) ?? new List<EshopStock>();
    public void SetEshopStockData(IList<EshopStock> value) => Write(SourceKeys.EshopStock, value);

    public IList<CatalogPurchaseRecord> GetPurchaseHistoryData() =>
        _cache.Get<List<CatalogPurchaseRecord>>(SourceKeys.PurchaseHistory) ?? new List<CatalogPurchaseRecord>();
    public void SetPurchaseHistoryData(IList<CatalogPurchaseRecord> value) => Write(SourceKeys.PurchaseHistory, value);

    public IList<ManufactureHistoryRecord> GetManufactureHistoryData() =>
        _cache.Get<List<ManufactureHistoryRecord>>(SourceKeys.ManufactureHistory) ?? new List<ManufactureHistoryRecord>();
    public void SetManufactureHistoryData(IList<ManufactureHistoryRecord> value) => Write(SourceKeys.ManufactureHistory, value);

    public IList<ConsumedMaterialRecord> GetConsumedData() =>
        _cache.Get<List<ConsumedMaterialRecord>>(SourceKeys.Consumed) ?? new List<ConsumedMaterialRecord>();
    public void SetConsumedData(IList<ConsumedMaterialRecord> value) => Write(SourceKeys.Consumed, value);

    public IList<StockTakingRecord> GetStockTakingData() =>
        _cache.Get<List<StockTakingRecord>>(SourceKeys.StockTaking) ?? new List<StockTakingRecord>();
    public void SetStockTakingData(IList<StockTakingRecord> value) => Write(SourceKeys.StockTaking, value);

    public IList<CatalogLot> GetLotsData() =>
        _cache.Get<List<CatalogLot>>(SourceKeys.Lots) ?? new List<CatalogLot>();
    public void SetLotsData(IList<CatalogLot> value) => Write(SourceKeys.Lots, value);

    public IList<ProductPriceEshop> GetEshopPriceData() =>
        _cache.Get<List<ProductPriceEshop>>(SourceKeys.EshopPrice) ?? new List<ProductPriceEshop>();
    public void SetEshopPriceData(IList<ProductPriceEshop> value) => Write(SourceKeys.EshopPrice, value);

    public IList<ProductPriceErp> GetErpPriceData() =>
        _cache.Get<List<ProductPriceErp>>(SourceKeys.ErpPrice) ?? new List<ProductPriceErp>();
    public void SetErpPriceData(IList<ProductPriceErp> value) => Write(SourceKeys.ErpPrice, value);

    public IList<ProductEshopUrl> GetEshopUrlData() =>
        _cache.Get<List<ProductEshopUrl>>(SourceKeys.EshopUrl) ?? new List<ProductEshopUrl>();
    public void SetEshopUrlData(IList<ProductEshopUrl> value) => Write(SourceKeys.EshopUrl, value);

    public IDictionary<string, List<ManufactureDifficultySetting>> GetManufactureDifficultySettingsData() =>
        _cache.Get<Dictionary<string, List<ManufactureDifficultySetting>>>(SourceKeys.ManufactureDifficultySettings)
        ?? new Dictionary<string, List<ManufactureDifficultySetting>>();
    public void SetManufactureDifficultySettingsData(IDictionary<string, List<ManufactureDifficultySetting>> value) =>
        Write(SourceKeys.ManufactureDifficultySettings, value);

    public DateTime? GetLoadDate(string sourceKey) =>
        _cache.Get<DateTime?>($"{sourceKey}_LoadDate");

    public DateTime? LastMergeDateTime => _cache.Get<DateTime?>(LastMergeDateTimeKey);

    public void SetLastMergeDateTime()
    {
        var mergeDateTime = _timeProvider.GetUtcNow().DateTime;
        _cache.Set(LastMergeDateTimeKey, mergeDateTime, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _cacheOptions.Value.CacheValidityPeriod
        });
    }

    private void Write<TValue>(string sourceKey, TValue value)
    {
        _cache.Set(sourceKey, value);
        InvalidateSourceData(sourceKey);
        SetLoadDateInCache(sourceKey);
    }

    private void InvalidateSourceData(string dataSource)
    {
        if (!_cacheOptions.Value.EnableBackgroundMerge)
        {
            _cache.Remove(CurrentCatalogCacheKey);
            _cache.Remove(StaleCatalogCacheKey);
            _cache.Remove(CacheUpdateTimeKey);
            return;
        }
        _mergeScheduler.ScheduleMerge(dataSource);
        _logger.LogDebug("Invalidated source data: {DataSource}", dataSource);
    }

    private void SetLoadDateInCache(string dataKey)
    {
        var loadDate = _timeProvider.GetUtcNow().DateTime;
        _cache.Set($"{dataKey}_LoadDate", loadDate, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _cacheOptions.Value.CacheValidityPeriod
        });
    }
}
