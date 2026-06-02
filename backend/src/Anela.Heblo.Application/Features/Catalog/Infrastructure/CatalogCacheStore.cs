using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
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

    // Per-source cache keys
    private const string CachedSalesDataKey = "CachedSalesData";
    private const string CachedCatalogAttributesDataKey = "CachedCatalogAttributesData";
    private const string CachedInTransportDataKey = "CachedInTransportData";
    private const string CachedManufacturedDataKey = "CachedManufacturedData";
    private const string CachedInReserveDataKey = "CachedInReserveData";
    private const string CachedInQuarantineDataKey = "CachedInQuarantineData";
    private const string CachedOrderedDataKey = "CachedOrderedData";
    private const string CachedPlannedDataKey = "CachedPlannedData";
    private const string CachedErpStockDataKey = "CachedErpStockData";
    private const string CachedEshopStockDataKey = "CachedEshopStockData";
    private const string CachedPurchaseHistoryDataKey = "CachedPurchaseHistoryData";
    private const string CachedManufactureHistoryDataKey = "CachedManufactureHistoryData";
    private const string CachedConsumedDataKey = "CachedConsumedData";
    private const string CachedStockTakingDataKey = "CachedStockTakingData";
    private const string CachedLotsDataKey = "CachedLotsData";
    private const string CachedEshopPriceDataKey = "CachedEshopPriceData";
    private const string CachedErpPriceDataKey = "CachedErpPriceData";
    private const string CachedEshopUrlDataKey = "CachedEshopUrlData";
    private const string CachedManufactureDifficultySettingsDataKey = "CachedManufactureDifficultySettingsData";

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
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _cacheOptions = cacheOptions ?? throw new ArgumentNullException(nameof(cacheOptions));
        _mergeScheduler = mergeScheduler ?? throw new ArgumentNullException(nameof(mergeScheduler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets catalog data: current if available, stale as fallback, empty with merge scheduled.
    /// </summary>
    public List<CatalogAggregate> GetCatalogData()
    {
        if (_cache.TryGetValue(CurrentCatalogCacheKey, out List<CatalogAggregate>? currentData) && currentData != null)
        {
            return currentData;
        }

        if (_cache.TryGetValue(StaleCatalogCacheKey, out List<CatalogAggregate>? staleData) && staleData != null)
        {
            try
            {
                _mergeScheduler.ScheduleMerge("CacheRead");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to schedule background merge when serving stale data");
            }

            return staleData;
        }

        _logger.LogWarning("No catalog data available in cache, triggering background merge");

        try
        {
            _mergeScheduler.ScheduleMerge("CacheEmpty");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule background merge for missing catalog data");
        }

        return new List<CatalogAggregate>();
    }

    /// <summary>
    /// Replaces the cache atomically: promotes current to stale, installs new data.
    /// </summary>
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
            _cache.Set(CacheUpdateTimeKey, _timeProvider.GetUtcNow().DateTime);

            _logger.LogDebug("Cache updated atomically with {ProductCount} products", newData.Count);
        }
        finally
        {
            _cacheReplacementSemaphore.Release();
        }
    }

    /// <summary>
    /// Checks if the current cache is still valid based on CacheValidityPeriod.
    /// </summary>
    public bool IsCacheValid()
    {
        var lastUpdate = _cache.Get<DateTime?>(CacheUpdateTimeKey);
        if (!lastUpdate.HasValue) return false;

        var timeSinceLastUpdate = _timeProvider.GetUtcNow().DateTime - lastUpdate.Value;
        return timeSinceLastUpdate < _cacheOptions.Value.CacheValidityPeriod;
    }

    /// <summary>
    /// Gets current catalog data from cache (does not fall back to stale).
    /// </summary>
    public List<CatalogAggregate>? TryGetCurrent() =>
        _cache.Get<List<CatalogAggregate>>(CurrentCatalogCacheKey);

    /// <summary>
    /// Gets stale catalog data from cache.
    /// </summary>
    public List<CatalogAggregate>? TryGetStale() =>
        _cache.Get<List<CatalogAggregate>>(StaleCatalogCacheKey);

    #region Per-Source Data Accessors

    public IList<CatalogSaleRecord> GetSalesData() =>
        _cache.Get<List<CatalogSaleRecord>>(CachedSalesDataKey) ?? new List<CatalogSaleRecord>();

    public void SetSalesData(IList<CatalogSaleRecord> value)
    {
        _cache.Set(CachedSalesDataKey, value);
        InvalidateSourceData(CachedSalesDataKey);
        SetLoadDateInCache(CachedSalesDataKey);
    }

    public IList<CatalogAttributes> GetCatalogAttributesData() =>
        _cache.Get<List<CatalogAttributes>>(CachedCatalogAttributesDataKey) ?? new List<CatalogAttributes>();

    public void SetCatalogAttributesData(IList<CatalogAttributes> value)
    {
        _cache.Set(CachedCatalogAttributesDataKey, value);
        InvalidateSourceData(CachedCatalogAttributesDataKey);
        SetLoadDateInCache(CachedCatalogAttributesDataKey);
    }

    public IDictionary<string, int> GetInTransportData() =>
        _cache.Get<Dictionary<string, int>>(CachedInTransportDataKey) ?? new Dictionary<string, int>();

    public void SetInTransportData(IDictionary<string, int> value)
    {
        _cache.Set(CachedInTransportDataKey, value);
        InvalidateSourceData(CachedInTransportDataKey);
        SetLoadDateInCache(CachedInTransportDataKey);
    }

    public IDictionary<string, decimal> GetManufacturedData() =>
        _cache.Get<Dictionary<string, decimal>>(CachedManufacturedDataKey) ?? new Dictionary<string, decimal>();

    public void SetManufacturedData(IDictionary<string, decimal> value)
    {
        _cache.Set(CachedManufacturedDataKey, value);
        InvalidateSourceData(CachedManufacturedDataKey);
        SetLoadDateInCache(CachedManufacturedDataKey);
    }

    public IDictionary<string, int> GetInReserveData() =>
        _cache.Get<Dictionary<string, int>>(CachedInReserveDataKey) ?? new Dictionary<string, int>();

    public void SetInReserveData(IDictionary<string, int> value)
    {
        _cache.Set(CachedInReserveDataKey, value);
        InvalidateSourceData(CachedInReserveDataKey);
        SetLoadDateInCache(CachedInReserveDataKey);
    }

    public IDictionary<string, int> GetInQuarantineData() =>
        _cache.Get<Dictionary<string, int>>(CachedInQuarantineDataKey) ?? new Dictionary<string, int>();

    public void SetInQuarantineData(IDictionary<string, int> value)
    {
        _cache.Set(CachedInQuarantineDataKey, value);
        InvalidateSourceData(CachedInQuarantineDataKey);
        SetLoadDateInCache(CachedInQuarantineDataKey);
    }

    public IDictionary<string, decimal> GetOrderedData() =>
        _cache.Get<Dictionary<string, decimal>>(CachedOrderedDataKey) ?? new Dictionary<string, decimal>();

    public void SetOrderedData(IDictionary<string, decimal> value)
    {
        _cache.Set(CachedOrderedDataKey, value);
        InvalidateSourceData(CachedOrderedDataKey);
        SetLoadDateInCache(CachedOrderedDataKey);
    }

    public IDictionary<string, decimal> GetPlannedData() =>
        _cache.Get<Dictionary<string, decimal>>(CachedPlannedDataKey) ?? new Dictionary<string, decimal>();

    public void SetPlannedData(IDictionary<string, decimal> value)
    {
        _cache.Set(CachedPlannedDataKey, value);
        InvalidateSourceData(CachedPlannedDataKey);
        SetLoadDateInCache(CachedPlannedDataKey);
    }

    public IList<ErpStock> GetErpStockData() =>
        _cache.Get<List<ErpStock>>(CachedErpStockDataKey) ?? new List<ErpStock>();

    public void SetErpStockData(IList<ErpStock> value)
    {
        _cache.Set(CachedErpStockDataKey, value);
        InvalidateSourceData(CachedErpStockDataKey);
        SetLoadDateInCache(CachedErpStockDataKey);
    }

    public IList<EshopStock> GetEshopStockData() =>
        _cache.Get<List<EshopStock>>(CachedEshopStockDataKey) ?? new List<EshopStock>();

    public void SetEshopStockData(IList<EshopStock> value)
    {
        _cache.Set(CachedEshopStockDataKey, value);
        InvalidateSourceData(CachedEshopStockDataKey);
        SetLoadDateInCache(CachedEshopStockDataKey);
    }

    public IList<CatalogPurchaseRecord> GetPurchaseHistoryData() =>
        _cache.Get<List<CatalogPurchaseRecord>>(CachedPurchaseHistoryDataKey) ?? new List<CatalogPurchaseRecord>();

    public void SetPurchaseHistoryData(IList<CatalogPurchaseRecord> value)
    {
        _cache.Set(CachedPurchaseHistoryDataKey, value);
        InvalidateSourceData(CachedPurchaseHistoryDataKey);
        SetLoadDateInCache(CachedPurchaseHistoryDataKey);
    }

    public IList<ManufactureHistoryRecord> GetManufactureHistoryData() =>
        _cache.Get<List<ManufactureHistoryRecord>>(CachedManufactureHistoryDataKey) ?? new List<ManufactureHistoryRecord>();

    public void SetManufactureHistoryData(IList<ManufactureHistoryRecord> value)
    {
        _cache.Set(CachedManufactureHistoryDataKey, value);
        InvalidateSourceData(CachedManufactureHistoryDataKey);
        SetLoadDateInCache(CachedManufactureHistoryDataKey);
    }

    public IList<ConsumedMaterialRecord> GetConsumedData() =>
        _cache.Get<List<ConsumedMaterialRecord>>(CachedConsumedDataKey) ?? new List<ConsumedMaterialRecord>();

    public void SetConsumedData(IList<ConsumedMaterialRecord> value)
    {
        _cache.Set(CachedConsumedDataKey, value);
        InvalidateSourceData(CachedConsumedDataKey);
        SetLoadDateInCache(CachedConsumedDataKey);
    }

    public IList<StockTakingRecord> GetStockTakingData() =>
        _cache.Get<List<StockTakingRecord>>(CachedStockTakingDataKey) ?? new List<StockTakingRecord>();

    public void SetStockTakingData(IList<StockTakingRecord> value)
    {
        _cache.Set(CachedStockTakingDataKey, value);
        InvalidateSourceData(CachedStockTakingDataKey);
        SetLoadDateInCache(CachedStockTakingDataKey);
    }

    public IList<CatalogLot> GetLotsData() =>
        _cache.Get<List<CatalogLot>>(CachedLotsDataKey) ?? new List<CatalogLot>();

    public void SetLotsData(IList<CatalogLot> value)
    {
        _cache.Set(CachedLotsDataKey, value);
        InvalidateSourceData(CachedLotsDataKey);
        SetLoadDateInCache(CachedLotsDataKey);
    }

    public IList<ProductPriceEshop> GetEshopPriceData() =>
        _cache.Get<List<ProductPriceEshop>>(CachedEshopPriceDataKey) ?? new List<ProductPriceEshop>();

    public void SetEshopPriceData(IList<ProductPriceEshop> value)
    {
        _cache.Set(CachedEshopPriceDataKey, value);
        InvalidateSourceData(CachedEshopPriceDataKey);
        SetLoadDateInCache(CachedEshopPriceDataKey);
    }

    public IList<ProductPriceErp> GetErpPriceData() =>
        _cache.Get<List<ProductPriceErp>>(CachedErpPriceDataKey) ?? new List<ProductPriceErp>();

    public void SetErpPriceData(IList<ProductPriceErp> value)
    {
        _cache.Set(CachedErpPriceDataKey, value);
        InvalidateSourceData(CachedErpPriceDataKey);
        SetLoadDateInCache(CachedErpPriceDataKey);
    }

    public IList<ProductEshopUrl> GetEshopUrlData() =>
        _cache.Get<List<ProductEshopUrl>>(CachedEshopUrlDataKey) ?? new List<ProductEshopUrl>();

    public void SetEshopUrlData(IList<ProductEshopUrl> value)
    {
        _cache.Set(CachedEshopUrlDataKey, value);
        InvalidateSourceData(CachedEshopUrlDataKey);
        SetLoadDateInCache(CachedEshopUrlDataKey);
    }

    public IDictionary<string, List<ManufactureDifficultySetting>> GetManufactureDifficultySettingsData() =>
        _cache.Get<Dictionary<string, List<ManufactureDifficultySetting>>>(CachedManufactureDifficultySettingsDataKey)
        ?? new Dictionary<string, List<ManufactureDifficultySetting>>();

    public void SetManufactureDifficultySettingsData(IDictionary<string, List<ManufactureDifficultySetting>> value)
    {
        _cache.Set(CachedManufactureDifficultySettingsDataKey, value);
        InvalidateSourceData(CachedManufactureDifficultySettingsDataKey);
        SetLoadDateInCache(CachedManufactureDifficultySettingsDataKey);
    }

    #endregion

    /// <summary>
    /// Gets the load date for a specific data source.
    /// </summary>
    public DateTime? GetLoadDateFromCache(string dataKey) =>
        _cache.Get<DateTime?>($"{dataKey}_LoadDate");

    /// <summary>
    /// Gets the last merge operation timestamp.
    /// </summary>
    public DateTime? LastMergeDateTime =>
        _cache.Get<DateTime?>(LastMergeDateTimeKey);

    /// <summary>
    /// Sets the last merge operation timestamp.
    /// </summary>
    public void SetLastMergeDateTime()
    {
        var mergeDateTime = _timeProvider.GetUtcNow().DateTime;
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _cacheOptions.Value.CacheValidityPeriod
        };
        _cache.Set(LastMergeDateTimeKey, mergeDateTime, cacheOptions);
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
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _cacheOptions.Value.CacheValidityPeriod
        };
        _cache.Set($"{dataKey}_LoadDate", loadDate, cacheOptions);
    }
}
