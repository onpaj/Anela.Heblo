using System.Linq.Expressions;
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog;

public sealed class CatalogRepository : ICatalogRepository
{
    private readonly CatalogCacheStore _cacheStore;
    private readonly CatalogMergeService _mergeService;
    private readonly CatalogHistoryRefreshService _historyRefreshService;
    private readonly CatalogStockRefreshService _stockRefreshService;
    private readonly CatalogMetaRefreshService _metaRefreshService;
    private readonly CatalogReferenceRefreshService _referenceRefreshService;
    private readonly ICatalogMergeScheduler _mergeScheduler;
    private readonly IMarginCalculationService _marginService;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<DataSourceOptions> _dataSourceOptions;
    private readonly IOptions<CatalogCacheOptions> _cacheOptions;
    private readonly ILogger<CatalogRepository> _logger;

    public CatalogRepository(
        CatalogCacheStore cacheStore,
        CatalogMergeService mergeService,
        CatalogHistoryRefreshService historyRefreshService,
        CatalogStockRefreshService stockRefreshService,
        CatalogMetaRefreshService metaRefreshService,
        CatalogReferenceRefreshService referenceRefreshService,
        ICatalogMergeScheduler mergeScheduler,
        IMarginCalculationService marginService,
        TimeProvider timeProvider,
        IOptions<DataSourceOptions> dataSourceOptions,
        IOptions<CatalogCacheOptions> cacheOptions,
        ILogger<CatalogRepository> logger)
    {
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _mergeService = mergeService ?? throw new ArgumentNullException(nameof(mergeService));
        _historyRefreshService = historyRefreshService ?? throw new ArgumentNullException(nameof(historyRefreshService));
        _stockRefreshService = stockRefreshService ?? throw new ArgumentNullException(nameof(stockRefreshService));
        _metaRefreshService = metaRefreshService ?? throw new ArgumentNullException(nameof(metaRefreshService));
        _referenceRefreshService = referenceRefreshService ?? throw new ArgumentNullException(nameof(referenceRefreshService));
        _mergeScheduler = mergeScheduler ?? throw new ArgumentNullException(nameof(mergeScheduler));
        _marginService = marginService ?? throw new ArgumentNullException(nameof(marginService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _dataSourceOptions = dataSourceOptions ?? throw new ArgumentNullException(nameof(dataSourceOptions));
        _cacheOptions = cacheOptions ?? throw new ArgumentNullException(nameof(cacheOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets catalog data with cache-valid/stale/priority-merge logic.
    /// </summary>
    private async Task<List<CatalogAggregate>> GetCatalogDataAsync(CancellationToken ct = default)
    {
        var current = _cacheStore.TryGetCurrent();
        if (current != null && _cacheStore.IsCacheValid())
            return current;

        // Merging again cannot repair a cache whose required sources have still never loaded - it
        // would read the same empty sources and produce the same aggregate, so a priority merge per
        // read would be pure cost. Serve what we have; loading a source schedules the merge that
        // restores validity on its own.
        if (current != null && !_cacheStore.AreRequiredSourcesLoaded())
        {
            _logger.LogDebug("Serving unstamped catalog data - required sources have not loaded yet, so a merge cannot improve it");
            return current;
        }

        if (_cacheOptions.Value.AllowStaleDataDuringMerge && _mergeScheduler.IsMergeInProgress)
        {
            var stale = _cacheStore.TryGetCompleteStale();
            if (stale != null)
            {
                _logger.LogWarning("Serving stale data during merge operation");
                return stale;
            }
        }

        return await _mergeService.ExecutePriorityMergeAsync();
    }

    // --- Query methods ---

    public Task<CatalogAggregate?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(_cacheStore.GetCatalogData().SingleOrDefault(s => s.ProductCode == id));

    public Task<IReadOnlyDictionary<string, CatalogAggregate>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var idSet = new HashSet<string>(ids);
        IReadOnlyDictionary<string, CatalogAggregate> result = _cacheStore.GetCatalogData()
            .Where(p => idSet.Contains(p.ProductCode))
            .ToDictionary(p => p.ProductCode, p => p);
        return Task.FromResult(result);
    }

    public async Task<IEnumerable<CatalogAggregate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await GetCatalogDataAsync(cancellationToken);
    }

    public Task<IEnumerable<CatalogAggregate>> FindAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(_cacheStore.GetCatalogData().AsQueryable().Where(predicate).AsEnumerable());

    public Task<CatalogAggregate?> SingleOrDefaultAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(_cacheStore.GetCatalogData().AsQueryable().SingleOrDefault(predicate));

    public Task<bool> AnyAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(_cacheStore.GetCatalogData().AsQueryable().Any(predicate));

    public Task<int> CountAsync(Expression<Func<CatalogAggregate, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var data = _cacheStore.GetCatalogData();
        return Task.FromResult(predicate == null ? data.Count : data.AsQueryable().Count(predicate));
    }

    public Task<List<CatalogAggregate>> GetProductsWithSalesInPeriod(DateTime fromDate, DateTime toDate, ProductType[] productTypes, CancellationToken cancellationToken = default)
    {
        var products = _cacheStore.GetCatalogData()
            .Where(p => productTypes.Contains(p.Type))
            // Synthetic bundle-component records carry no revenue, so a product whose only in-period
            // sales are synthetic would enter the margin/analytics stream with an empty history.
            .Where(p => p.SalesHistory.Any(s => s.SourceBundleCode == null && s.Date >= fromDate && s.Date <= toDate))
            .ToList();
        return Task.FromResult(products);
    }

    // --- Refresh delegates ---

    public Task RefreshTransportData(CancellationToken ct) => _stockRefreshService.RefreshTransportData(ct);
    public Task RefreshManufacturedData(CancellationToken ct) => _stockRefreshService.RefreshManufacturedData(ct);
    public Task RefreshReserveData(CancellationToken ct) => _stockRefreshService.RefreshReserveData(ct);
    public Task RefreshOrderedData(CancellationToken ct) => _stockRefreshService.RefreshOrderedData(ct);
    public Task RefreshPlannedData(CancellationToken ct) => _stockRefreshService.RefreshPlannedData(ct);
    public Task RefreshSalesData(CancellationToken ct) => _historyRefreshService.RefreshSalesData(ct);
    public Task RefreshSetPartsData(CancellationToken ct) => _historyRefreshService.RefreshSetPartsData(ct);
    public Task RefreshAttributesData(CancellationToken ct) => _metaRefreshService.RefreshAttributesData(ct);
    public Task RefreshErpStockData(CancellationToken ct) => _stockRefreshService.RefreshErpStockData(ct);
    public Task RefreshEshopStockData(CancellationToken ct) => _stockRefreshService.RefreshEshopStockData(ct);
    public Task RefreshPurchaseHistoryData(CancellationToken ct) => _historyRefreshService.RefreshPurchaseHistoryData(ct);
    public Task RefreshManufactureHistoryData(CancellationToken ct) => _historyRefreshService.RefreshManufactureHistoryData(ct);
    public Task RefreshConsumedHistoryData(CancellationToken ct) => _historyRefreshService.RefreshConsumedHistoryData(ct);
    public Task RefreshStockTakingData(CancellationToken ct) => _referenceRefreshService.RefreshStockTakingData(ct);
    public Task RefreshLotsData(CancellationToken ct) => _metaRefreshService.RefreshLotsData(ct);
    public Task RefreshEshopPricesData(CancellationToken ct) => _metaRefreshService.RefreshEshopPricesData(ct);
    public Task RefreshErpPricesData(CancellationToken ct) => _metaRefreshService.RefreshErpPricesData(ct);
    public Task RefreshEshopUrlData(CancellationToken ct) => _metaRefreshService.RefreshEshopUrlData(ct);
    public Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct) =>
        _referenceRefreshService.RefreshManufactureDifficultySettingsData(product, ct);

    public async Task RefreshMarginData(CancellationToken ct)
    {
        await WaitForCurrentMergeAsync(ct);
        var products = await GetAllAsync(ct);

        // The window must match what the cost providers can emit: each of them derives its own
        // window from DataSourceOptions.ManufactureCostHistoryDays, and a margin month with no cost
        // data enters MonthlyMarginHistory.Averages as a zero, scaling every displayed cost down.
        // Not ManufactureHistoryDays - that one only controls how much raw manufacture history is
        // loaded into the catalog.
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var dateFrom = today.AddDays(-_dataSourceOptions.Value.ManufactureCostHistoryDays);
        var dateTo = today.AddMonths(-1); // Current month is not accurate

        if (dateFrom > dateTo)
        {
            _logger.LogWarning(
                "DataSourceOptions.ManufactureCostHistoryDays ({Days}) covers no completed month, so margin history will be empty for every product",
                _dataSourceOptions.Value.ManufactureCostHistoryDays);
        }

        foreach (var product in products)
        {
            product.Margins = await _marginService.GetMarginAsync(product, dateFrom, dateTo, ct);
        }
    }

    // --- Load date properties ---

    public DateTime? TransportLoadDate => _cacheStore.GetLoadDateFromCache("CachedInTransportData");
    public DateTime? ManufacturedLoadDate => _cacheStore.GetLoadDateFromCache("CachedManufacturedData");
    public DateTime? ReserveLoadDate => _cacheStore.GetLoadDateFromCache("CachedInReserveData");
    public DateTime? QuarantineLoadDate => _cacheStore.GetLoadDateFromCache("CachedInQuarantineData");
    public DateTime? OrderedLoadDate => _cacheStore.GetLoadDateFromCache("CachedOrderedData");
    public DateTime? PlannedLoadDate => _cacheStore.GetLoadDateFromCache("CachedPlannedData");
    public DateTime? SalesLoadDate => _cacheStore.GetLoadDateFromCache("CachedSalesData");
    public DateTime? AttributesLoadDate => _cacheStore.GetLoadDateFromCache("CachedCatalogAttributesData");
    public DateTime? ErpStockLoadDate => _cacheStore.GetLoadDateFromCache("CachedErpStockData");
    public DateTime? EshopStockLoadDate => _cacheStore.GetLoadDateFromCache("CachedEshopStockData");
    public DateTime? PurchaseHistoryLoadDate => _cacheStore.GetLoadDateFromCache("CachedPurchaseHistoryData");
    public DateTime? ManufactureHistoryLoadDate => _cacheStore.GetLoadDateFromCache("CachedManufactureHistoryData");
    public DateTime? ConsumedHistoryLoadDate => _cacheStore.GetLoadDateFromCache("CachedConsumedData");
    public DateTime? StockTakingLoadDate => _cacheStore.GetLoadDateFromCache("CachedStockTakingData");
    public DateTime? LotsLoadDate => _cacheStore.GetLoadDateFromCache("CachedLotsData");
    public DateTime? EshopPricesLoadDate => _cacheStore.GetLoadDateFromCache("CachedEshopPriceData");
    public DateTime? ErpPricesLoadDate => _cacheStore.GetLoadDateFromCache("CachedErpPriceData");
    public DateTime? EshopUrlLoadDate => _cacheStore.GetLoadDateFromCache("CachedEshopUrlData");
    public DateTime? ManufactureDifficultySettingsLoadDate => _cacheStore.GetLoadDateFromCache("CachedManufactureDifficultySettingsData");

    // --- Merge tracking ---

    public DateTime? LastMergeDateTime => _cacheStore.LastMergeDateTime;

    public bool ChangesPendingForMerge
    {
        get
        {
            var lastMerge = LastMergeDateTime;
            if (lastMerge == null) return true;

            var loadDates = new DateTime?[]
            {
                TransportLoadDate, ManufacturedLoadDate, ReserveLoadDate, QuarantineLoadDate,
                OrderedLoadDate, PlannedLoadDate, SalesLoadDate, AttributesLoadDate,
                ErpStockLoadDate, EshopStockLoadDate, PurchaseHistoryLoadDate, ManufactureHistoryLoadDate,
                ConsumedHistoryLoadDate, StockTakingLoadDate, LotsLoadDate, EshopPricesLoadDate,
                ErpPricesLoadDate, EshopUrlLoadDate, ManufactureDifficultySettingsLoadDate,
            };

            if (loadDates.Any(d => d == null)) return true;
            return loadDates.Where(d => d.HasValue).Max(d => d!.Value) > lastMerge;
        }
    }

    public Task WaitForCurrentMergeAsync(CancellationToken cancellationToken = default)
        => _mergeScheduler.WaitForCurrentMergeAsync(cancellationToken);
}
