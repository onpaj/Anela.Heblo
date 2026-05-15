namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public interface IProductCatalogQueryService
{
    Task<IReadOnlyList<ProductCatalogEntry>> GetActiveProductsAsync(CancellationToken ct = default);
}
