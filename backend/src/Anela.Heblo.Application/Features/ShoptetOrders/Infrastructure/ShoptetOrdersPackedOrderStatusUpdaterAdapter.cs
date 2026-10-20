using Anela.Heblo.Application.Features.Packaging.Contracts;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure;

internal sealed class ShoptetOrdersPackedOrderStatusUpdaterAdapter : IPackedOrderStatusUpdater
{
    private readonly IEshopOrderClient _eshopOrderClient;

    public ShoptetOrdersPackedOrderStatusUpdaterAdapter(IEshopOrderClient eshopOrderClient)
    {
        _eshopOrderClient = eshopOrderClient;
    }

    public Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default)
        => _eshopOrderClient.MarkAsPackedAsync(orderCode, ct);
}
