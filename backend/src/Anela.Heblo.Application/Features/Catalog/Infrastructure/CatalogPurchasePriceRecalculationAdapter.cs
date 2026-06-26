using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Price;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class CatalogPurchasePriceRecalculationAdapter : IPurchasePriceRecalculationService
{
    private readonly IProductPriceErpClient _productPriceErpClient;

    public CatalogPurchasePriceRecalculationAdapter(IProductPriceErpClient productPriceErpClient)
    {
        _productPriceErpClient = productPriceErpClient;
    }

    public Task RecalculatePurchasePriceAsync(int bomId, CancellationToken cancellationToken) =>
        _productPriceErpClient.RecalculatePurchasePrice(bomId, cancellationToken);
}
