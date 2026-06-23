using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

public interface IShoptetExpeditionOrderSource
{
    Task<OrderListResponse> GetOrdersByStatusAsync(int statusId, int page, CancellationToken ct = default);
    Task<ExpeditionOrderDetail> GetExpeditionOrderDetailAsync(string code, CancellationToken ct = default);
    Task<OrderSummary?> GetOrderByCodeAsync(string code, CancellationToken ct = default);
    Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
    Task UpdateStatusAsync(string orderCode, int statusId, CancellationToken ct = default);
    Task SetAdditionalFieldAsync(string orderCode, int index, string? text, CancellationToken ct = default);
    Task<string> GetEshopRemarkAsync(string orderCode, CancellationToken ct = default);
    Task UpdateEshopRemarkAsync(string orderCode, string eshopRemark, CancellationToken ct = default);
}
