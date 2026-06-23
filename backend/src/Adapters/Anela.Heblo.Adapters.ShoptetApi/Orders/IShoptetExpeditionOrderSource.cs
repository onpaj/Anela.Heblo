using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

public interface IShoptetExpeditionOrderSource
{
    Task<OrderListResponse> GetOrdersByStatusAsync(int statusId, int page, CancellationToken ct = default);
    Task<ExpeditionOrderDetail> GetExpeditionOrderDetailAsync(string code, CancellationToken ct = default);
    Task SetAdditionalFieldAsync(string orderCode, int index, string? text, CancellationToken ct = default);
}
