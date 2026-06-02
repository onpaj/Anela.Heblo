using System.Linq.Expressions;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog;

public class CatalogRepository : ICatalogRepository
{
    private readonly CatalogCacheStore _cacheStore;
    private readonly CatalogMergeService _mergeService;
    private readonly CatalogDataRefreshService _refreshService;
    private readonly ICatalogMergeScheduler _mergeScheduler;

    public CatalogRepository(
        CatalogCacheStore cacheStore,
        CatalogMergeService mergeService,
        CatalogDataRefreshService refreshService,
        ICatalogMergeScheduler mergeScheduler)
    {
        _cacheStore = cacheStore;
        _mergeService = mergeService;
        _refreshService = refreshService;
        _mergeScheduler = mergeScheduler;
    }

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
        var data = await GetCatalogDataAsync();
        return data.AsEnumerable();
    }

    public Task<IEnumerable<CatalogAggregate>> FindAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(_cacheStore.GetCatalogData().AsQueryable().Where(predicate).AsEnumerable());

    public Task<CatalogAggregate?> SingleOrDefaultAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(_cacheStore.GetCatalogData().AsQueryable().SingleOrDefault(predicate));

    public Task<bool> AnyAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(_cacheStore.GetCatalogData().AsQueryable().Any(predicate));

    public Task<int> CountAsync(Expression<Func<CatalogAggregate, bool>>? predicate = null, CancellationToken cancellationToken = default)
        => Task.FromResult(predicate == null ? _cacheStore.GetCatalogData().Count : _cacheStore.GetCatalogData().AsQueryable().Count(predicate));

    public Task<List<CatalogAggregate>> GetProductsWithSalesInPeriod(
        DateTime fromDate,
        DateTime toDate,
        ProductType[] productTypes,
        CancellationToken cancellationToken = default)
    {
        var products = _cacheStore.GetCatalogData()
            .Where(p => productTypes.Contains(p.Type))
            .Where(p => p.SalesHistory.Any(s => s.Date >= fromDate && s.Date <= toDate))
            .ToList();
        return Task.FromResult(products);
    }

    private async Task<List<CatalogAggregate>> GetCatalogDataAsync()
    {
        var current = _cacheStore.TryGetCurrent();
        if (current != null && _cacheStore.IsCacheValid())
            return current;

        if (_mergeScheduler.IsMergeInProgress)
        {
            var stale = _cacheStore.TryGetStale();
            if (stale != null) return stale;
        }

        return await _mergeService.ExecutePriorityMergeAsync();
    }

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
    public Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct)
        => _refreshService.RefreshManufactureDifficultySettingsData(product, ct);

    public DateTime? TransportLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.InTransport);
    public DateTime? ManufacturedLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.Manufactured);
    public DateTime? ReserveLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.InReserve);
    public DateTime? QuarantineLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.InQuarantine);
    public DateTime? OrderedLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.Ordered);
    public DateTime? PlannedLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.Planned);
    public DateTime? SalesLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.Sales);
    public DateTime? AttributesLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.Attributes);
    public DateTime? ErpStockLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.ErpStock);
    public DateTime? EshopStockLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.EshopStock);
    public DateTime? PurchaseHistoryLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.PurchaseHistory);
    public DateTime? ManufactureHistoryLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.ManufactureHistory);
    public DateTime? ConsumedHistoryLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.Consumed);
    public DateTime? StockTakingLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.StockTaking);
    public DateTime? LotsLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.Lots);
    public DateTime? EshopPricesLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.EshopPrice);
    public DateTime? ErpPricesLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.ErpPrice);
    public DateTime? EshopUrlLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.EshopUrl);
    public DateTime? ManufactureDifficultySettingsLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.ManufactureDifficultySettings);

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
                ErpStockLoadDate, EshopStockLoadDate, PurchaseHistoryLoadDate,
                ManufactureHistoryLoadDate, ConsumedHistoryLoadDate, StockTakingLoadDate,
                LotsLoadDate, EshopPricesLoadDate, ErpPricesLoadDate, EshopUrlLoadDate,
                ManufactureDifficultySettingsLoadDate,
            };
            if (loadDates.Any(d => d == null)) return true;
            var maxLoadDate = loadDates.Where(d => d.HasValue).Max(d => d!.Value);
            return maxLoadDate > lastMerge;
        }
    }

    public Task WaitForCurrentMergeAsync(CancellationToken cancellationToken = default)
        => _mergeScheduler.WaitForCurrentMergeAsync(cancellationToken);
}
