namespace Anela.Heblo.Domain.Features.Catalog.Price;

public interface IProductPriceEshopClient
{
    Task<IEnumerable<ProductPriceEshop>> GetAllAsync(CancellationToken cancellationToken);
    Task<SetProductPricesResultDto> SetAllAsync(IEnumerable<ProductPriceEshop> eshopData, CancellationToken cancellationToken);
}