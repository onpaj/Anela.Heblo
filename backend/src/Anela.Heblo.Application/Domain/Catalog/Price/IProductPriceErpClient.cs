namespace Anela.Heblo.Application.Domain.Catalog.Price;

public interface IProductPriceErpClient
{
    Task<IEnumerable<ProductPriceErp>> GetAllAsync(bool forceReload, CancellationToken cancellationToken);
}