namespace Anela.Heblo.Application.Features.ShoptetOrders;

public interface IEshopOrderClient
{
    Task<string> CreateOrderAsync(CreateEshopOrderRequest request, CancellationToken ct = default);
    Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
    Task UpdateStatusAsync(string orderCode, int statusId, CancellationToken ct = default);
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
    /// Transitions the order to the configured "packed" state
    /// (Shoptet "Zabaleno", id 52 by default). Called by the Balení screen
    /// after a successful scan + shipment creation.
    /// </summary>
    Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);
}
