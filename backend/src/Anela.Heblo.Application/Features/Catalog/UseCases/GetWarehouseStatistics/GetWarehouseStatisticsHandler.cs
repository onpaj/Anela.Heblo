using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetWarehouseStatistics;

public class GetWarehouseStatisticsHandler : IRequestHandler<GetWarehouseStatisticsRequest, GetWarehouseStatisticsResponse>
{
    private readonly ICatalogRepository _catalogRepository;

    public GetWarehouseStatisticsHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
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

        // Warehouse capacity constant
        const double warehouseCapacityKg = 3000;

        // Calculate utilization percentage (can exceed 100%)
        var utilizationPercentage = warehouseCapacityKg > 0 ? (totalWeight / warehouseCapacityKg) * 100 : 0;

        // Count total products
        var totalProductCount = allCatalogItems.Count();

        return new GetWarehouseStatisticsResponse
        {
            TotalQuantity = totalQuantity,
            TotalWeight = totalWeight,
            WarehouseCapacityKg = warehouseCapacityKg,
            WarehouseUtilizationPercentage = utilizationPercentage,
            TotalProductCount = totalProductCount,
            LastUpdated = DateTime.UtcNow
        };
    }
}