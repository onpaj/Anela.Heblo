namespace Anela.Heblo.Domain.Features.ShoptetOrders;

public interface IShoptetOrderClient
{
    Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
    Task UpdateStatusAsync(string orderCode, int statusId, CancellationToken ct = default);
    Task SetInternalNoteAsync(string orderCode, string note, CancellationToken ct = default);
}
