using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Catalog;

public interface ICatalogRepository : IReadOnlyRepository<CatalogAggregate, string>
{
    Task RefreshTransportData(CancellationToken ct);
    Task RefreshReserveData(CancellationToken ct);
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
    Task RefreshManufactureDifficultyData(CancellationToken ct);
}