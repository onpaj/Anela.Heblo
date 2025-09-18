using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Catalog;

public interface ICatalogRepository : IReadOnlyRepository<CatalogAggregate, string>
{
    Task RefreshTransportData(CancellationToken ct);
    Task RefreshReserveData(CancellationToken ct);
    Task RefreshOrderedData(CancellationToken ct);
    Task RefreshSalesData(CancellationToken ct);
    Task RefreshAttributesData(CancellationToken ct);
    Task RefreshErpStockData(CancellationToken ct);
    Task RefreshEshopStockData(CancellationToken ct);
    Task RefreshPurchaseHistoryData(CancellationToken ct);
    Task RefreshManufactureHistoryData(CancellationToken ct);
    Task RefreshConsumedHistoryData(CancellationToken ct);
    Task RefreshStockTakingData(CancellationToken ct);
    Task RefreshLotsData(CancellationToken ct);
    Task RefreshEshopPricesData(CancellationToken ct);
    Task RefreshErpPricesData(CancellationToken ct);
    Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct);

    // Data load timestamps - stored in cache with same expiration as data
    DateTime? TransportLoadDate { get; }
    DateTime? ReserveLoadDate { get; }
    DateTime? OrderedLoadDate { get; }
    DateTime? SalesLoadDate { get; }
    DateTime? AttributesLoadDate { get; }
    DateTime? ErpStockLoadDate { get; }
    DateTime? EshopStockLoadDate { get; }
    DateTime? PurchaseHistoryLoadDate { get; }
    DateTime? ManufactureHistoryLoadDate { get; }
    DateTime? ConsumedHistoryLoadDate { get; }
    DateTime? StockTakingLoadDate { get; }
    DateTime? LotsLoadDate { get; }
    DateTime? EshopPricesLoadDate { get; }
    DateTime? ErpPricesLoadDate { get; }
    DateTime? ManufactureDifficultySettingsLoadDate { get; }
    DateTime? ManufactureCostLoadDate { get; }
    
    // Merge operation tracking
    DateTime? LastMergeDateTime { get; }
    bool ChangesPendingForMerge { get; }

    // Analytics methods
    Task<List<CatalogAggregate>> GetProductsWithSalesInPeriod(
        DateTime fromDate,
        DateTime toDate,
        ProductType[] productTypes,
        CancellationToken cancellationToken = default);
}