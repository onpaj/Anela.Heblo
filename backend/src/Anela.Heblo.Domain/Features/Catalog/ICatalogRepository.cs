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

    // Data loaded flags - set once when cached data is populated
    bool TransportDataLoaded { get; }
    bool ReserveDataLoaded { get; }
    bool OrderedDataLoaded { get; }
    bool SalesDataLoaded { get; }
    bool AttributesDataLoaded { get; }
    bool ErpStockDataLoaded { get; }
    bool EshopStockDataLoaded { get; }
    bool PurchaseHistoryDataLoaded { get; }
    bool ManufactureHistoryDataLoaded { get; }
    bool ConsumedHistoryDataLoaded { get; }
    bool StockTakingDataLoaded { get; }
    bool LotsDataLoaded { get; }
    bool EshopPricesDataLoaded { get; }
    bool ErpPricesDataLoaded { get; }
    bool ManufactureDifficultySettingsDataLoaded { get; }
    bool ManufactureDifficultyDataLoaded { get; }
    bool ManufactureCostDataLoaded { get; }

    // Analytics methods
    Task<List<CatalogAggregate>> GetProductsWithSalesInPeriod(
        DateTime fromDate,
        DateTime toDate,
        ProductType[] productTypes,
        CancellationToken cancellationToken = default);
}