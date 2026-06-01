namespace Anela.Heblo.Domain.Features.Catalog.EshopUrl;

public interface IProductEshopUrlClient
{
    Task<IEnumerable<ProductEshopUrl>> GetAllAsync(CancellationToken cancellationToken);
}
