namespace Anela.Heblo.Application.Domain.Catalog.Price;

public interface IProductPriceEshopClient
{
    Task<IEnumerable<ProductPriceEshop>> GetAllAsync(CancellationToken cancellationToken);
    Task<SetProductPricesResultDto> SetAllAsync(IEnumerable<ProductPriceEshop> eshopData, CancellationToken cancellationToken);
}