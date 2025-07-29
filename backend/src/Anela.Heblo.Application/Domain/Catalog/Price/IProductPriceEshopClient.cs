using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Anela.Heblo.Price;

public interface IProductPriceEshopClient 
{
    Task<IEnumerable<ProductPriceEshop>> GetAllAsync(CancellationToken cancellationToken);
    Task<SetProductPricesResultDto> SetAllAsync(IEnumerable<ProductPriceEshop> eshopData, CancellationToken cancellationToken);
}