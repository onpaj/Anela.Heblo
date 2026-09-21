using Anela.Heblo.Application.Features.ExpeditionList.Contracts;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure;

internal sealed class ShoptetOrdersOrderStatusReaderAdapter : IOrderStatusReader
{
    private readonly IEshopOrderClient _eshopOrderClient;

    public ShoptetOrdersOrderStatusReaderAdapter(IEshopOrderClient eshopOrderClient)
    {
        _eshopOrderClient = eshopOrderClient;
    }

    public Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default)
        => _eshopOrderClient.GetOrderStatusIdAsync(orderCode, ct);
}
