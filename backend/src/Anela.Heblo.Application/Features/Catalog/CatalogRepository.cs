using System.Linq.Expressions;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog;

public sealed class CatalogRepository : ICatalogRepository
{
    private readonly CatalogCacheStore _cacheStore;
    private readonly CatalogMergeService _mergeService;
    private readonly CatalogDataRefreshService _refreshService;
    private readonly ICatalogMergeScheduler _mergeScheduler;
    private readonly IOptions<CatalogCacheOptions> _cacheOptions;
    private readonly ILogger<CatalogRepository> _logger;

    public CatalogRepository(
        CatalogCacheStore cacheStore,
        CatalogMergeService mergeService,
        CatalogDataRefreshService refreshService,
        ICatalogMergeScheduler mergeScheduler,
        IOptions<CatalogCacheOptions> cacheOptions,
        ILogger<CatalogRepository> logger)
    {
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _mergeService = mergeService ?? throw new ArgumentNullException(nameof(mergeService));
        _refreshService = refreshService ?? throw new ArgumentNullException(nameof(refreshService));
        _mergeScheduler = mergeScheduler ?? throw new ArgumentNullException(nameof(mergeScheduler));
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

        if (_cacheOptions.Value.AllowStaleDataDuringMerge && _mergeScheduler.IsMergeInProgress)
        {
            var stale = _cacheStore.TryGetStale();
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
            .Where(p => p.SalesHistory.Any(s => s.Date >= fromDate && s.Date <= toDate))
            .ToList();
        return Task.FromResult(products);
    }

    // --- Refresh delegates ---

    public Task RefreshTransportData(CancellationToken ct) => _refreshService.RefreshTransportData(ct);
    public Task RefreshManufacturedData(CancellationToken ct) => _refreshService.RefreshManufacturedData(ct);
    public Task RefreshReserveData(CancellationToken ct) => _refreshService.RefreshReserveData(ct);
    public Task RefreshOrderedData(CancellationToken ct) => _refreshService.RefreshOrderedData(ct);
    public Task RefreshPlannedData(CancellationToken ct) => _refreshService.RefreshPlannedData(ct);
    public Task RefreshSalesData(CancellationToken ct) => _refreshService.RefreshSalesData(ct);
    public Task RefreshAttributesData(CancellationToken ct) => _refreshService.RefreshAttributesData(ct);
    public Task RefreshErpStockData(CancellationToken ct) => _refreshService.RefreshErpStockData(ct);
    public Task RefreshEshopStockData(CancellationToken ct) => _refreshService.RefreshEshopStockData(ct);
    public Task RefreshPurchaseHistoryData(CancellationToken ct) => _refreshService.RefreshPurchaseHistoryData(ct);
    public Task RefreshManufactureHistoryData(CancellationToken ct) => _refreshService.RefreshManufactureHistoryData(ct);
    public Task RefreshConsumedHistoryData(CancellationToken ct) => _refreshService.RefreshConsumedHistoryData(ct);
    public Task RefreshStockTakingData(CancellationToken ct) => _refreshService.RefreshStockTakingData(ct);
    public Task RefreshLotsData(CancellationToken ct) => _refreshService.RefreshLotsData(ct);
    public Task RefreshEshopPricesData(CancellationToken ct) => _refreshService.RefreshEshopPricesData(ct);
    public Task RefreshErpPricesData(CancellationToken ct) => _refreshService.RefreshErpPricesData(ct);
    public Task RefreshEshopUrlData(CancellationToken ct) => _refreshService.RefreshEshopUrlData(ct);
    public Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct) =>
        _refreshService.RefreshManufactureDifficultySettingsData(product, ct);

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
