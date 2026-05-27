namespace Anela.Heblo.Application.Features.ShoptetOrders;

public interface IEshopOrderClient
{
    Task<string> CreateOrderAsync(CreateEshopOrderRequest request, CancellationToken ct = default);
    Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
    Task UpdateStatusAsync(string orderCode, int statusId, CancellationToken ct = default);
    Task SetInternalNoteAsync(string orderCode, string note, CancellationToken ct = default);

    /// <summary>
    /// Returns the current internal (staff-facing) remark for the given order,
    /// as returned by GET /api/orders/{code}?include=notes → data.order.notes.eshopRemark.
    /// Returns an empty string if Shoptet sends null or the notes object is absent.
    /// </summary>
    Task<string> GetEshopRemarkAsync(string orderCode, CancellationToken ct = default);

    /// <summary>
    /// Overwrites the order's internal (staff-facing) remark via
    /// PATCH /api/orders/{code}/notes with body {"data":{"eshopRemark":"..."}}.
    /// The caller is responsible for preserving any existing content (read-modify-write).
    /// </summary>
    Task UpdateEshopRemarkAsync(string orderCode, string eshopRemark, CancellationToken ct = default);

    Task DeleteOrderAsync(string orderCode, CancellationToken ct = default);
    Task<List<EshopOrderSummary>> GetRecentOrdersAsync(int count = 20, CancellationToken ct = default);
    Task<List<EshopOrderSummary>> ListByExternalCodePrefixAsync(string prefix, string? emailFilter = null, CancellationToken ct = default);

    /// <summary>
    /// Gets the first <paramref name="count"/> orders for the given email from the most recent page.
    /// Filters in-memory (Shoptet API does not support email filter on list endpoint).
    /// </summary>
    Task<List<EshopOrderInfo>> GetRecentOrdersByEmailAsync(string email, int count, CancellationToken ct = default);

    /// <summary>
    /// Returns a map of status id → status name from GET /api/eshop?include=orderStatuses.
    /// </summary>
    Task<Dictionary<int, string>> GetOrderStatusNamesAsync(CancellationToken ct = default);
}
