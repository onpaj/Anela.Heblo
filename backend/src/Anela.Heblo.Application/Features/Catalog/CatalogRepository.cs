using System.Linq.Expressions;
using System.Collections.Concurrent;
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Purchase;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog;

public class CatalogRepository : ICatalogRepository
{
    private readonly ICatalogSalesClient _salesClient;
    private readonly ICatalogAttributesClient _attributesClient;
    private readonly IEshopStockClient _eshopStockClient;
    private readonly IConsumedMaterialsClient _consumedMaterialClient;
    private readonly IPurchaseHistoryClient _purchaseHistoryClient;
    private readonly IErpStockClient _erpStockClient;
    private readonly ILotsClient _lotsClient;
    private readonly IProductPriceEshopClient _productPriceEshopClient;
    private readonly IProductPriceErpClient _productPriceErpClient;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly IManufactureRepository _manufactureRepository;
    private readonly IPurchaseOrderRepository _purchaseOrderRepository;
    private readonly IManufactureOrderRepository _manufactureOrderRepository;
    private readonly IManufactureHistoryClient _manufactureHistoryClient;
    private readonly IManufactureCostCalculationService _manufactureCostCalculationService;
    private readonly IManufactureDifficultyRepository _manufactureDifficultyRepository;
    private readonly ICatalogResilienceService _resilienceService;
    private readonly ICatalogMergeScheduler _mergeScheduler;

    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<DataSourceOptions> _options;
    private readonly IOptions<CatalogCacheOptions> _cacheOptions;
    private readonly ILogger<CatalogRepository> _logger;

    // Cache keys
    private const string CurrentCatalogCacheKey = "CatalogData_Current";
    private const string StaleCatalogCacheKey = "CatalogData_Stale";
    private const string CacheUpdateTimeKey = "CatalogData_LastUpdate";

    private readonly SemaphoreSlim _cacheReplacementSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTime> _sourceLastUpdated = new();


    public CatalogRepository(
        ICatalogSalesClient salesClient,
        ICatalogAttributesClient attributesClient,
        IEshopStockClient eshopStockClient,
        IConsumedMaterialsClient consumedMaterialClient,
        IPurchaseHistoryClient purchaseHistoryClient,
        IErpStockClient erpStockClient,
        ILotsClient lotsClient,
        IProductPriceEshopClient productPriceEshopClient,
        IProductPriceErpClient productPriceErpClient,
        ITransportBoxRepository transportBoxRepository,
        IStockTakingRepository stockTakingRepository,
        IManufactureRepository manufactureRepository,
        IPurchaseOrderRepository purchaseOrderRepository,
        IManufactureOrderRepository manufactureOrderRepository,
        IManufactureHistoryClient manufactureHistoryClient,
        IManufactureCostCalculationService manufactureCostCalculationService,
        IManufactureDifficultyRepository manufactureDifficultyRepository,
        ICatalogResilienceService resilienceService,
        ICatalogMergeScheduler mergeScheduler,
        IMemoryCache cache,
        TimeProvider timeProvider,
        IOptions<DataSourceOptions> _options,
        IOptions<CatalogCacheOptions> cacheOptions,
        ILogger<CatalogRepository> logger)
    {
        _salesClient = salesClient;
        _attributesClient = attributesClient;
        _eshopStockClient = eshopStockClient;
        _consumedMaterialClient = consumedMaterialClient;
        _purchaseHistoryClient = purchaseHistoryClient;
        _erpStockClient = erpStockClient;
        _lotsClient = lotsClient;
        _productPriceEshopClient = productPriceEshopClient;
        _productPriceErpClient = productPriceErpClient;
        _transportBoxRepository = transportBoxRepository;
        _stockTakingRepository = stockTakingRepository;
        _manufactureRepository = manufactureRepository;
        _purchaseOrderRepository = purchaseOrderRepository;
        _manufactureOrderRepository = manufactureOrderRepository;
        _manufactureHistoryClient = manufactureHistoryClient;
        _manufactureCostCalculationService = manufactureCostCalculationService;
        _manufactureDifficultyRepository = manufactureDifficultyRepository;
        _resilienceService = resilienceService;
        _mergeScheduler = mergeScheduler;
        _cache = cache;
        _timeProvider = timeProvider;
        this._options = _options;
        _cacheOptions = cacheOptions;
        _logger = logger;

        // Initialize merge callback to avoid circular dependency
        _mergeScheduler.SetMergeCallback(ExecuteBackgroundMergeAsync);
    }

    public async Task RefreshTransportData(CancellationToken ct)
    {
        var transportData = await GetProductsInTransport(ct);
        CachedInTransportData = transportData;
    }

    public async Task RefreshReserveData(CancellationToken ct)
    {
        var reserveData = await GetProductsInReserve(ct);
        CachedInReserveData = reserveData;
    }

    public async Task RefreshOrderedData(CancellationToken ct)
    {
        var orderedData = await GetProductsOrdered(ct);
        CachedOrderedData = orderedData;
    }

    public async Task RefreshPlannedData(CancellationToken ct)
    {
        var plannedData = await GetProductsPlanned(ct);
        CachedPlannedData = plannedData;
    }

    public async Task RefreshSalesData(CancellationToken ct)
    {
        CachedSalesData = await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => await _salesClient.GetAsync(
                _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.SalesHistoryDays),
                _timeProvider.GetUtcNow().Date,
                cancellationToken: cancellationToken),
            "RefreshSalesData", ct);
    }

    public async Task RefreshAttributesData(CancellationToken ct)
    {
        CachedCatalogAttributesData = await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => await _attributesClient.GetAttributesAsync(cancellationToken: cancellationToken),
            "RefreshAttributesData", ct);
    }

    public async Task RefreshErpStockData(CancellationToken ct)
    {
        CachedErpStockData = await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => (await _erpStockClient.ListAsync(cancellationToken)).ToList(),
            "RefreshErpStockData", ct);
    }

    public async Task RefreshEshopStockData(CancellationToken ct)
    {
        CachedEshopStockData = await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => (await _eshopStockClient.ListAsync(cancellationToken)).ToList(),
            "RefreshEshopStockData", ct);
    }

    public async Task RefreshPurchaseHistoryData(CancellationToken ct)
    {
        CachedPurchaseHistoryData = (await _purchaseHistoryClient.GetHistoryAsync(null, _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.PurchaseHistoryDays), _timeProvider.GetUtcNow().Date, cancellationToken: ct))
            .ToList();
    }

    public async Task RefreshConsumedHistoryData(CancellationToken ct)
    {
        CachedConsumedData = (await _consumedMaterialClient.GetConsumedAsync(_timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ConsumedHistoryDays), _timeProvider.GetUtcNow().Date, cancellationToken: ct))
            .ToList();
    }

    public async Task RefreshStockTakingData(CancellationToken ct)
    {
        CachedStockTakingData = (await _stockTakingRepository.GetAllAsync(ct)).ToList();
    }

    public async Task RefreshLotsData(CancellationToken ct)
    {
        CachedLotsData = (await _lotsClient.GetAsync(cancellationToken: ct)).ToList();
    }

    public async Task RefreshEshopPricesData(CancellationToken ct)
    {
        CachedEshopPriceData = (await _productPriceEshopClient.GetAllAsync(ct)).ToList();
    }

    public async Task RefreshErpPricesData(CancellationToken ct)
    {
        CachedErpPriceData = (await _productPriceErpClient.GetAllAsync(false, ct)).ToList();
    }

    public async Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct)
    {
        // Load all manufacture difficulty history records
        var difficultySettings = await _manufactureDifficultyRepository.ListAsync(product, cancellationToken: ct);

        if (product == null) // All
        {
            // Group by product code for efficient lookup
            CachedManufactureDifficultySettingsData = difficultySettings
                .GroupBy(h => h.ProductCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.ValidFrom ?? DateTime.MinValue).ToList());
        }
        else
        {
            CachedManufactureDifficultySettingsData[product] = difficultySettings;
            CatalogData.SingleOrDefault(s => s.ProductCode == product)?.ManufactureDifficultySettings.Assign(difficultySettings, _timeProvider.GetUtcNow().UtcDateTime);
        }
    }

    public async Task RefreshManufactureHistoryData(CancellationToken ct)
    {
        CachedManufactureHistoryData = (await _manufactureHistoryClient.GetHistoryAsync(_timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ManufactureHistoryDays), _timeProvider.GetUtcNow().Date, cancellationToken: ct))
            .ToList();
    }

    public async Task RefreshManufactureCostData(CancellationToken ct)
    {
        // Add ManufactureHistory data
        var manufactureMap = CachedManufactureHistoryData
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());

        foreach (var product in CatalogData)
        {
            if (manufactureMap.TryGetValue(product.ProductCode, out var manufactures))
            {
                product.ManufactureHistory = manufactures.ToList();
            }
        }

        // Calculate costs
        CachedManufactureCostData = await _manufactureCostCalculationService.CalculateManufactureCostHistoryAsync(CatalogData, ct);
    }

    private async Task<List<CatalogAggregate>> GetCatalogDataAsync()
    {
        // Return current cache if available and valid
        var currentCache = _cache.Get<List<CatalogAggregate>>(CurrentCatalogCacheKey);
        if (currentCache != null && IsCacheValid())
        {
            return currentCache;
        }

        // If merge is in progress and stale data is allowed, return stale data
        if (_cacheOptions.Value.AllowStaleDataDuringMerge && _mergeScheduler.IsMergeInProgress)
        {
            var staleCache = _cache.Get<List<CatalogAggregate>>(StaleCatalogCacheKey);
            if (staleCache != null)
            {
                _logger.LogWarning("Serving stale data during merge operation");
                return staleCache;
            }
        }

        // No cache available or cache invalid - execute priority merge and wait
        return await ExecutePriorityMergeAsync();
    }

    private List<CatalogAggregate> CatalogData
    {
        get
        {
            // Try current cache first
            if (_cache.TryGetValue(CurrentCatalogCacheKey, out List<CatalogAggregate>? currentData) && currentData != null)
            {
                return currentData;
            }

            // Try stale cache as fallback
            if (_cache.TryGetValue(StaleCatalogCacheKey, out List<CatalogAggregate>? staleData) && staleData != null)
            {
                // Trigger background merge to refresh data, but don't wait
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

            // Last resort: return empty list and trigger background merge
            // Don't block here - let the background process handle it
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
    }

    private List<CatalogAggregate> Merge()
    {
        var products = CachedErpStockData.Select(s => new CatalogAggregate()
        {
            ProductCode = s.ProductCode,
            ProductName = s.ProductName,
            ErpId = s.ProductId,
            Stock = new StockData()
            {
                Erp = s.Stock
            },
            Type = GetProductType(s),
            MinimalOrderQuantity = s.MOQ,
            HasLots = s.HasLots,
            HasExpiration = s.HasExpiration,
            Volume = s.Volume,
            NetWeight = s.Weight,
            Note = s.Note,
            SupplierCode = s.SupplierCode,
            SupplierName = s.SupplierName,
        }).ToList();

        // First populate all other data for products
        var attributesMap = CachedCatalogAttributesData.ToDictionary(k => k.ProductCode, v => v);
        var eshopProductsMap = CachedEshopStockData.ToDictionary(k => k.Code, v => v);
        var consumedMap = CachedConsumedData
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());
        var purchaseMap = CachedPurchaseHistoryData
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());
        var manufactureMap = CachedManufactureHistoryData
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());
        var stockTakingMap = CachedStockTakingData
            .GroupBy(p => p.Code)
            .ToDictionary(k => k.Key, v => v.ToList());
        var lotsMap = CachedLotsData
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());
        var eshopPriceMap = CachedEshopPriceData.ToDictionary(k => k.ProductCode, v => v);
        var erpPriceMap = CachedErpPriceData.ToDictionary(k => k.ProductCode, v => v);

        foreach (var product in products)
        {
            product.SalesHistory = CachedSalesData.Where(w => w.ProductCode == product.ProductCode).ToList();

            if (attributesMap.TryGetValue(product.ProductCode, out var attributes))
            {
                product.Properties.OptimalStockDaysSetup = attributes.OptimalStockDays;
                product.Properties.StockMinSetup = attributes.StockMin;
                product.Properties.BatchSize = attributes.BatchSize;
                product.Properties.ExpirationMonths = attributes.ExpirationMonths;
                product.Properties.SeasonMonths = attributes.SeasonMonthsArray;
                product.MinimalManufactureQuantity = attributes.MinimalManufactureQuantity;
            }

            if (CachedInTransportData.TryGetValue(product.ProductCode, out var inTransport))
            {
                product.Stock.Transport = inTransport;
            }

            if (CachedInReserveData.TryGetValue(product.ProductCode, out var inReserve))
            {
                product.Stock.Reserve = inReserve;
            }

            if (CachedOrderedData.TryGetValue(product.ProductCode, out var ordered))
            {
                product.Stock.Ordered = ordered;
            }

            if (CachedPlannedData.TryGetValue(product.ProductCode, out var planned))
            {
                product.Stock.Planned = planned;
            }

            if (eshopProductsMap.TryGetValue(product.ProductCode, out var eshopProduct))
            {
                product.Stock.Eshop = eshopProduct.Stock;
                product.Stock.PrimaryStockSource = StockSource.Eshop;
                product.Location = eshopProduct.Location;
                product.Image = eshopProduct.Image;
                product.DefaultImage = eshopProduct.DefaultImage;
                product.GrossWeight = eshopProduct.Weight;
                product.Height = eshopProduct.Height;
                product.Width = eshopProduct.Width;
                product.Depth = eshopProduct.Depth;
                product.AtypicalShipping = eshopProduct.AtypicalShipping;
            }

            if (consumedMap.TryGetValue(product.ProductCode, out var consumed))
            {
                product.ConsumedHistory = consumed.ToList();
            }

            if (purchaseMap.TryGetValue(product.ProductCode, out var purchases))
            {
                product.PurchaseHistory = purchases.ToList();
            }

            if (manufactureMap.TryGetValue(product.ProductCode, out var manufactures))
            {
                product.ManufactureHistory = manufactures.ToList();
            }

            if (stockTakingMap.TryGetValue(product.ProductCode, out var stockTakings))
            {
                product.StockTakingHistory = stockTakings.OrderByDescending(o => o.Date).ToList();
            }

            if (lotsMap.TryGetValue(product.ProductCode, out var lots))
            {
                product.Stock.Lots = lots.ToList();
            }

            // Mapování eshop cen podle ProductCode
            if (eshopPriceMap.TryGetValue(product.ProductCode, out var eshopPrice))
            {
                product.EshopPrice = eshopPrice;
            }

            // Mapování ERP cen podle ProductCode
            if (erpPriceMap.TryGetValue(product.ProductCode, out var erpPrice))
            {
                product.ErpPrice = erpPrice;
            }

            // Set ManufactureDifficultySettings with historical data and current value
            if (CachedManufactureDifficultySettingsData.TryGetValue(product.ProductCode, out var difficultySettings))
            {
                product.ManufactureDifficultySettings.Assign(difficultySettings.ToList(), _timeProvider.GetUtcNow().UtcDateTime);
            }

            if (CachedManufactureCostData.TryGetValue(product.ProductCode, out var costHistory))
            {
                product.ManufactureCostHistory = costHistory.ToList();
                if (product.ErpPrice != null)
                    product.ManufactureCostHistory.ForEach(f => f.MaterialCostFromPurchasePrice = product.ErpPrice.PurchasePrice);
            }

            // Calculate margin after all data (including EshopPrice and ManufactureCostHistory) is populated
            product.UpdateMarginCalculation();
        }

        // Set last merge timestamp
        SetLastMergeDateTime();

        return products.ToList();
    }

    private static ProductType GetProductType(ErpStock s)
    {
        var type = (ProductType?)s.ProductTypeId ?? ProductType.UNDEFINED;

        if (type == ProductType.Product && (s.ProductCode.StartsWith("BAL") || s.ProductCode.StartsWith("SET")))
            return ProductType.Set;

        return type;
    }

    public async Task ExecuteBackgroundMergeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var newCatalogData = await Task.Run(() => Merge(), cancellationToken);
            await ReplaceCacheAtomicallyAsync(newCatalogData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background merge failed");
            throw;
        }
    }

    private async Task<List<CatalogAggregate>> ExecutePriorityMergeAsync()
    {
        _logger.LogInformation("Executing priority merge - no cache available");

        var newCatalogData = await Task.Run(() => Merge());
        await ReplaceCacheAtomicallyAsync(newCatalogData);

        return newCatalogData;
    }

    private async Task ReplaceCacheAtomicallyAsync(List<CatalogAggregate> newData)
    {
        await _cacheReplacementSemaphore.WaitAsync();
        try
        {
            // Keep current as stale fallback
            var currentCache = _cache.Get<List<CatalogAggregate>>(CurrentCatalogCacheKey);
            if (currentCache != null)
            {
                var staleExpiry = _cacheOptions.Value.StaleDataRetentionPeriod;
                _cache.Set(StaleCatalogCacheKey, currentCache, staleExpiry);
            }

            // Replace with fresh data
            _cache.Set(CurrentCatalogCacheKey, newData);
            _cache.Set(CacheUpdateTimeKey, DateTime.UtcNow);

            _logger.LogDebug("Cache updated atomically with {ProductCount} products", newData.Count);
        }
        finally
        {
            _cacheReplacementSemaphore.Release();
        }
    }

    private void InvalidateSourceData(string dataSource)
    {
        if (!_cacheOptions.Value.EnableBackgroundMerge)
        {
            // Fallback to old behavior if background merge is disabled
            _cache.Remove(CurrentCatalogCacheKey);
            _cache.Remove(StaleCatalogCacheKey);
            _cache.Remove(CacheUpdateTimeKey);
            return;
        }

        _sourceLastUpdated[dataSource] = DateTime.UtcNow;
        _mergeScheduler.ScheduleMerge(dataSource);

        _logger.LogDebug("Invalidated source data: {DataSource}", dataSource);
    }

    private bool IsCacheValid()
    {
        var lastUpdate = _cache.Get<DateTime?>(CacheUpdateTimeKey);
        if (!lastUpdate.HasValue) return false;

        var timeSinceLastUpdate = DateTime.UtcNow - lastUpdate.Value;
        return timeSinceLastUpdate < _cacheOptions.Value.CacheValidityPeriod;
    }

    private IList<CatalogSaleRecord> CachedSalesData
    {
        get => _cache.Get<List<CatalogSaleRecord>>(nameof(CachedSalesData)) ?? new List<CatalogSaleRecord>();
        set
        {
            _cache.Set(nameof(CachedSalesData), value);
            InvalidateSourceData(nameof(CachedSalesData));
            SetLoadDateInCache(nameof(CachedSalesData));
        }
    }



    private IList<CatalogAttributes> CachedCatalogAttributesData
    {
        get => _cache.Get<List<CatalogAttributes>>(nameof(CachedCatalogAttributesData)) ?? new List<CatalogAttributes>();
        set
        {
            _cache.Set(nameof(CachedCatalogAttributesData), value);
            InvalidateSourceData(nameof(CachedCatalogAttributesData));
            SetLoadDateInCache(nameof(CachedCatalogAttributesData));
        }
    }
    private IDictionary<string, int> CachedInTransportData
    {
        get => _cache.Get<Dictionary<string, int>>(nameof(CachedInTransportData)) ?? new Dictionary<string, int>();
        set
        {
            _cache.Set(nameof(CachedInTransportData), value);
            InvalidateSourceData(nameof(CachedInTransportData));
            SetLoadDateInCache(nameof(CachedInTransportData));
        }
    }

    private IDictionary<string, int> CachedInReserveData
    {
        get => _cache.Get<Dictionary<string, int>>(nameof(CachedInReserveData)) ?? new Dictionary<string, int>();
        set
        {
            _cache.Set(nameof(CachedInReserveData), value);
            InvalidateSourceData(nameof(CachedInReserveData));
            SetLoadDateInCache(nameof(CachedInReserveData));
        }
    }

    private IDictionary<string, decimal> CachedOrderedData
    {
        get => _cache.Get<Dictionary<string, decimal>>(nameof(CachedOrderedData)) ?? new Dictionary<string, decimal>();
        set
        {
            _cache.Set(nameof(CachedOrderedData), value);
            InvalidateSourceData(nameof(CachedOrderedData));
            SetLoadDateInCache(nameof(CachedOrderedData));
        }
    }

    private IDictionary<string, decimal> CachedPlannedData
    {
        get => _cache.Get<Dictionary<string, decimal>>(nameof(CachedPlannedData)) ?? new Dictionary<string, decimal>();
        set
        {
            _cache.Set(nameof(CachedPlannedData), value);
            InvalidateSourceData(nameof(CachedPlannedData));
            SetLoadDateInCache(nameof(CachedPlannedData));
        }
    }

    private IList<ErpStock> CachedErpStockData
    {
        get => _cache.Get<List<ErpStock>>(nameof(CachedErpStockData)) ?? new List<ErpStock>();
        set
        {
            _cache.Set(nameof(CachedErpStockData), value);
            InvalidateSourceData(nameof(CachedErpStockData));
            SetLoadDateInCache(nameof(CachedErpStockData));
        }
    }
    private IList<EshopStock> CachedEshopStockData
    {
        get => _cache.Get<List<EshopStock>>(nameof(CachedEshopStockData)) ?? new List<EshopStock>();
        set
        {
            _cache.Set(nameof(CachedEshopStockData), value);
            InvalidateSourceData(nameof(CachedEshopStockData));
            SetLoadDateInCache(nameof(CachedEshopStockData));
        }
    }
    private IList<CatalogPurchaseRecord> CachedPurchaseHistoryData
    {
        get => _cache.Get<List<CatalogPurchaseRecord>>(nameof(CachedPurchaseHistoryData)) ?? new List<CatalogPurchaseRecord>();
        set
        {
            _cache.Set(nameof(CachedPurchaseHistoryData), value);
            InvalidateSourceData(nameof(CachedPurchaseHistoryData));
            SetLoadDateInCache(nameof(CachedPurchaseHistoryData));
        }
    }
    private IList<ManufactureHistoryRecord> CachedManufactureHistoryData
    {
        get => _cache.Get<List<ManufactureHistoryRecord>>(nameof(CachedManufactureHistoryData)) ?? new List<ManufactureHistoryRecord>();
        set
        {
            _cache.Set(nameof(CachedManufactureHistoryData), value);
            InvalidateSourceData(nameof(CachedManufactureHistoryData));
            SetLoadDateInCache(nameof(CachedManufactureHistoryData));
        }
    }
    private IList<ConsumedMaterialRecord> CachedConsumedData
    {
        get => _cache.Get<List<ConsumedMaterialRecord>>(nameof(CachedConsumedData)) ?? new List<ConsumedMaterialRecord>();
        set
        {
            _cache.Set(nameof(CachedConsumedData), value);
            InvalidateSourceData(nameof(CachedConsumedData));
            SetLoadDateInCache(nameof(CachedConsumedData));
        }
    }

    private IList<StockTakingRecord> CachedStockTakingData
    {
        get => _cache.Get<List<StockTakingRecord>>(nameof(CachedStockTakingData)) ?? new List<StockTakingRecord>();
        set
        {
            _cache.Set(nameof(CachedStockTakingData), value);
            InvalidateSourceData(nameof(CachedStockTakingData));
            SetLoadDateInCache(nameof(CachedStockTakingData));
        }
    }

    private IList<CatalogLot> CachedLotsData
    {
        get => _cache.Get<List<CatalogLot>>(nameof(CachedLotsData)) ?? new List<CatalogLot>();
        set
        {
            _cache.Set(nameof(CachedLotsData), value);
            InvalidateSourceData(nameof(CachedLotsData));
            SetLoadDateInCache(nameof(CachedLotsData));
        }
    }

    private IList<ProductPriceEshop> CachedEshopPriceData
    {
        get => _cache.Get<List<ProductPriceEshop>>(nameof(CachedEshopPriceData)) ?? new List<ProductPriceEshop>();
        set
        {
            _cache.Set(nameof(CachedEshopPriceData), value);
            InvalidateSourceData(nameof(CachedEshopPriceData));
            SetLoadDateInCache(nameof(CachedEshopPriceData));
        }
    }

    private IList<ProductPriceErp> CachedErpPriceData
    {
        get => _cache.Get<List<ProductPriceErp>>(nameof(CachedErpPriceData)) ?? new List<ProductPriceErp>();
        set
        {
            _cache.Set(nameof(CachedErpPriceData), value);
            InvalidateSourceData(nameof(CachedErpPriceData));
            SetLoadDateInCache(nameof(CachedErpPriceData));
        }
    }

    private IDictionary<string, List<ManufactureCost>> CachedManufactureCostData
    {
        get => _cache.Get<Dictionary<string, List<ManufactureCost>>>(nameof(CachedManufactureCostData)) ?? new Dictionary<string, List<ManufactureCost>>();
        set
        {
            _cache.Set(nameof(CachedManufactureCostData), value);
            InvalidateSourceData(nameof(CachedManufactureCostData));
            SetLoadDateInCache(nameof(CachedManufactureCostData));
        }
    }

    private IDictionary<string, List<ManufactureDifficultySetting>> CachedManufactureDifficultySettingsData
    {
        get => _cache.Get<Dictionary<string, List<ManufactureDifficultySetting>>>(nameof(CachedManufactureDifficultySettingsData)) ?? new Dictionary<string, List<ManufactureDifficultySetting>>();
        set
        {
            _cache.Set(nameof(CachedManufactureDifficultySettingsData), value);
            InvalidateSourceData(nameof(CachedManufactureDifficultySettingsData));
            SetLoadDateInCache(nameof(CachedManufactureDifficultySettingsData));
        }
    }

    // Data load timestamps - stored in cache with same expiration as data
    public DateTime? TransportLoadDate => GetLoadDateFromCache(nameof(CachedInTransportData));
    public DateTime? ReserveLoadDate => GetLoadDateFromCache(nameof(CachedInReserveData));
    public DateTime? OrderedLoadDate => GetLoadDateFromCache(nameof(CachedOrderedData));
    public DateTime? PlannedLoadDate => GetLoadDateFromCache(nameof(CachedPlannedData));
    public DateTime? SalesLoadDate => GetLoadDateFromCache(nameof(CachedSalesData));
    public DateTime? AttributesLoadDate => GetLoadDateFromCache(nameof(CachedCatalogAttributesData));
    public DateTime? ErpStockLoadDate => GetLoadDateFromCache(nameof(CachedErpStockData));
    public DateTime? EshopStockLoadDate => GetLoadDateFromCache(nameof(CachedEshopStockData));
    public DateTime? PurchaseHistoryLoadDate => GetLoadDateFromCache(nameof(CachedPurchaseHistoryData));
    public DateTime? ManufactureHistoryLoadDate => GetLoadDateFromCache(nameof(CachedManufactureHistoryData));
    public DateTime? ConsumedHistoryLoadDate => GetLoadDateFromCache(nameof(CachedConsumedData));
    public DateTime? StockTakingLoadDate => GetLoadDateFromCache(nameof(CachedStockTakingData));
    public DateTime? LotsLoadDate => GetLoadDateFromCache(nameof(CachedLotsData));
    public DateTime? EshopPricesLoadDate => GetLoadDateFromCache(nameof(CachedEshopPriceData));
    public DateTime? ErpPricesLoadDate => GetLoadDateFromCache(nameof(CachedErpPriceData));
    public DateTime? ManufactureDifficultySettingsLoadDate => GetLoadDateFromCache(nameof(CachedManufactureDifficultySettingsData));
    public DateTime? ManufactureCostLoadDate => GetLoadDateFromCache(nameof(CachedManufactureCostData));

    // Merge operation tracking
    public DateTime? LastMergeDateTime => _cache.Get<DateTime?>("LastMergeDateTime");

    public bool ChangesPendingForMerge
    {
        get
        {
            var lastMerge = LastMergeDateTime;

            // If no merge has been performed yet, changes are pending
            if (lastMerge == null)
                return true;

            // Get all LoadDate properties
            var loadDates = new DateTime?[]
            {
                TransportLoadDate,
                ReserveLoadDate,
                OrderedLoadDate,
                PlannedLoadDate,
                SalesLoadDate,
                AttributesLoadDate,
                ErpStockLoadDate,
                EshopStockLoadDate,
                PurchaseHistoryLoadDate,
                ManufactureHistoryLoadDate,
                ConsumedHistoryLoadDate,
                StockTakingLoadDate,
                LotsLoadDate,
                EshopPricesLoadDate,
                ErpPricesLoadDate,
                ManufactureDifficultySettingsLoadDate,
            };

            // If any LoadDate is null, changes are pending
            if (loadDates.Any(date => date == null))
                return true;

            // If any LoadDate is greater than LastMergeDateTime, changes are pending
            var maxLoadDate = loadDates.Where(date => date.HasValue).Max(date => date.Value);
            return maxLoadDate > lastMerge;
        }
    }

    private DateTime? GetLoadDateFromCache(string dataKey)
    {
        return _cache.Get<DateTime?>($"{dataKey}_LoadDate");
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

    private void SetLastMergeDateTime()
    {
        var mergeDateTime = _timeProvider.GetUtcNow().DateTime;
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _cacheOptions.Value.CacheValidityPeriod
        };
        _cache.Set("LastMergeDateTime", mergeDateTime, cacheOptions);
    }

    private async Task<Dictionary<string, int>> GetProductsInTransport(CancellationToken ct)
    {
        var boxes = await _transportBoxRepository.FindAsync(TransportBox.IsInTransportPredicate, includeDetails: true, cancellationToken: ct);
        return boxes.SelectMany(s => s.Items)
            .GroupBy(g => g.ProductCode)
            .ToDictionary(k => k.Key, v => v.Sum(s => (int)s.Amount));
    }

    private async Task<Dictionary<string, int>> GetProductsInReserve(CancellationToken ct)
    {
        var boxes = await _transportBoxRepository.FindAsync(TransportBox.IsInReservePredicate, includeDetails: true, cancellationToken: ct);
        return boxes.SelectMany(s => s.Items)
            .GroupBy(g => g.ProductCode)
            .ToDictionary(k => k.Key, v => v.Sum(s => (int)s.Amount));
    }

    private async Task<Dictionary<string, decimal>> GetProductsOrdered(CancellationToken ct)
    {
        return await _purchaseOrderRepository.GetOrderedQuantitiesAsync(ct);
    }

    private async Task<Dictionary<string, decimal>> GetProductsPlanned(CancellationToken ct)
    {
        return await _manufactureOrderRepository.GetPlannedQuantitiesAsync(ct);
    }

    public Task<CatalogAggregate?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(CatalogData.SingleOrDefault(s => s.ProductCode == id));
    public async Task<IEnumerable<CatalogAggregate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var catalogData = await GetCatalogDataAsync();
        return catalogData.AsEnumerable();
    }
    public Task<IEnumerable<CatalogAggregate>> FindAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(CatalogData.AsQueryable().Where(predicate).AsEnumerable());
    public Task<CatalogAggregate?> SingleOrDefaultAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(CatalogData.AsQueryable().SingleOrDefault(predicate));
    public Task<bool> AnyAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(CatalogData.AsQueryable().Any(predicate));
    public Task<int> CountAsync(Expression<Func<CatalogAggregate, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(predicate == null ? CatalogData.Count : CatalogData.AsQueryable().Count(predicate));

    public Task<List<CatalogAggregate>> GetProductsWithSalesInPeriod(
        DateTime fromDate,
        DateTime toDate,
        ProductType[] productTypes,
        CancellationToken cancellationToken = default)
    {
        var products = CatalogData
            .Where(p => productTypes.Contains(p.Type))
            .Where(p => p.SalesHistory.Any(s => s.Date >= fromDate && s.Date <= toDate))
            .ToList();

        return Task.FromResult(products);
    }

}


