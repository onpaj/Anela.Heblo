using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public sealed class ProductCatalogQueryService : IProductCatalogQueryService
{
    private readonly ICatalogRepository _repository;

    public ProductCatalogQueryService(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ProductCatalogEntry>> GetActiveProductsAsync(CancellationToken ct = default)
    {
        var products = await _repository.FindAsync(
            p => p.Type == ProductType.Product || p.Type == ProductType.Goods,
            ct);

        return products
            .Select(p => new ProductCatalogEntry
            {
                ProductCode = p.ProductCode,
                ProductName = p.ProductName,
                Url = p.Url
            })
            .ToList();
    }
}
