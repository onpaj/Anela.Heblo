using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase.Infrastructure;

internal sealed class PurchaseCatalogSourceAdapter : ICatalogPurchaseSource
{
    private readonly IPurchaseOrderRepository _purchaseOrderRepository;

    public PurchaseCatalogSourceAdapter(IPurchaseOrderRepository purchaseOrderRepository)
    {
        _purchaseOrderRepository = purchaseOrderRepository;
    }

    public Task<Dictionary<string, decimal>> GetOrderedQuantitiesAsync(CancellationToken cancellationToken) =>
        _purchaseOrderRepository.GetOrderedQuantitiesAsync(cancellationToken);
}
