using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetWarehouseStatistics;

public class GetWarehouseStatisticsHandler : IRequestHandler<GetWarehouseStatisticsRequest, GetWarehouseStatisticsResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly TimeProvider _timeProvider;

    public GetWarehouseStatisticsHandler(ICatalogRepository catalogRepository, TimeProvider timeProvider)
    {
        _catalogRepository = catalogRepository;
        _timeProvider = timeProvider;
    }

    public async Task<GetWarehouseStatisticsResponse> Handle(GetWarehouseStatisticsRequest request, CancellationToken cancellationToken)
    {
        // Get all catalog items
        var allCatalogItems = (await _catalogRepository.GetAllAsync()).Where(w => w.Type == ProductType.Product || w.Type == ProductType.Goods).ToList();

        // Calculate total quantity from eshop stock
        var totalQuantity = allCatalogItems.Sum(item => item.Stock.Eshop);

        // Calculate total weight (quantity * weight for items with weight)
        var totalWeight = allCatalogItems
            .Where(item => item.GrossWeight.HasValue)
            .Sum(item => (double)item.Stock.Eshop * item.GrossWeight!.Value / 1000.0);

        // Calculate utilization percentage (can exceed 100%)
        var utilizationPercentage = CatalogConstants.WarehouseCapacityKg > 0
            ? (totalWeight / CatalogConstants.WarehouseCapacityKg) * 100
            : 0;

        // Count total products
        var totalProductCount = allCatalogItems.Count();

        return new GetWarehouseStatisticsResponse
        {
            TotalQuantity = totalQuantity,
            TotalWeight = totalWeight,
            WarehouseCapacityKg = CatalogConstants.WarehouseCapacityKg,
            WarehouseUtilizationPercentage = utilizationPercentage,
            TotalProductCount = totalProductCount,
            LastUpdated = _timeProvider.GetUtcNow().UtcDateTime
        };
    }
}