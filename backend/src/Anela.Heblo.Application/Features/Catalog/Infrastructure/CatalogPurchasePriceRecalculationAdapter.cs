using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class CatalogPurchasePriceRecalculationAdapter : IPurchasePriceRecalculationService
{
    private readonly IProductPriceErpClient _productPriceErpClient;
    private readonly IErpPurchasePriceWriter _purchasePriceWriter;

    public CatalogPurchasePriceRecalculationAdapter(
        IProductPriceErpClient productPriceErpClient,
        IErpPurchasePriceWriter purchasePriceWriter)
    {
        _productPriceErpClient = productPriceErpClient;
        _purchasePriceWriter = purchasePriceWriter;
    }

    public Task RecalculatePurchasePriceAsync(int bomId, CancellationToken cancellationToken) =>
        _productPriceErpClient.RecalculatePurchasePrice(bomId, cancellationToken);

    public Task SetPurchasePriceAsync(int erpItemId, decimal purchasePrice, CancellationToken cancellationToken) =>
        _purchasePriceWriter.SetPurchasePriceAsync(erpItemId, purchasePrice, cancellationToken);
}
