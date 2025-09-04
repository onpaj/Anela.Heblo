using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetMaterialForPurchase;

public class GetMaterialsForPurchaseHandler : IRequestHandler<GetMaterialsForPurchaseRequest, GetMaterialsForPurchaseResponse>
{
    private readonly ICatalogRepository _catalogRepository;

    public GetMaterialsForPurchaseHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task<GetMaterialsForPurchaseResponse> Handle(GetMaterialsForPurchaseRequest request, CancellationToken cancellationToken)
    {
        // Get all materials and goods (types suitable for purchase orders)
        var catalogItems = await _catalogRepository.FindAsync(
            item => item.Type == ProductType.Material || item.Type == ProductType.Goods,
            cancellationToken);

        // Filter by search term if provided
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var searchTerm = request.SearchTerm.ToLowerInvariant();
            catalogItems = catalogItems.Where(item =>
                item.ProductCode.ToLowerInvariant().Contains(searchTerm) ||
                item.ProductName.ToLowerInvariant().Contains(searchTerm));
        }

        // Take limited results and map to DTOs
        var materials = catalogItems
            .Take(request.Limit)
            .Select(item => new MaterialForPurchaseDto
            {
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                ProductType = item.Type.ToString(),
                LastPurchasePrice = item.PurchaseHistory.LastOrDefault()?.PricePerPiece,
                Location = string.IsNullOrEmpty(item.Location) ? null : item.Location,
                CurrentStock = (int)item.Stock.Available,
                MinimalOrderQuantity = string.IsNullOrEmpty(item.MinimalOrderQuantity) ? null : item.MinimalOrderQuantity
            })
            .OrderBy(m => m.ProductName)
            .ToList();

        return new GetMaterialsForPurchaseResponse
        {
            Materials = materials
        };
    }
}