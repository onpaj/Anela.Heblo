namespace Anela.Heblo.Domain.Features.Catalog.Price;

public interface IProductPriceErpClient
{
    Task<IEnumerable<ProductPriceErp>> GetAllAsync(bool forceReload, CancellationToken cancellationToken);

    Task RecalculatePurchasePrice(int bomId, CancellationToken cancellationToken);
}