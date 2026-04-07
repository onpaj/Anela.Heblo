namespace Anela.Heblo.Application.Features.ShoptetOrders;

public interface IEshopOrderClient
{
    Task<string> CreateOrderAsync(CreateEshopOrderRequest request, CancellationToken ct = default);
    Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
    Task UpdateStatusAsync(string orderCode, int statusId, CancellationToken ct = default);
    Task SetInternalNoteAsync(string orderCode, string note, CancellationToken ct = default);
    Task DeleteOrderAsync(string orderCode, CancellationToken ct = default);
    Task<List<EshopOrderSummary>> GetRecentOrdersAsync(int count = 20, CancellationToken ct = default);
    Task<List<EshopOrderSummary>> ListByExternalCodePrefixAsync(string prefix, string? emailFilter = null, CancellationToken ct = default);
}
