namespace Anela.Heblo.Application.Features.ShoptetOrders;

public interface IEshopOrderClient
{
    Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
    Task UpdateStatusAsync(string orderCode, int statusId, CancellationToken ct = default);
    /// <summary>
    /// Changes the order status without the e-mail and SMS the status may be set up to send to the
    /// customer (Shoptet <c>suppressEmailSending</c> / <c>suppressSmsSending</c>). Linked documents
    /// are still generated as for a normal status change.
    /// </summary>
    Task UpdateStatusWithoutNotificationsAsync(string orderCode, int statusId, CancellationToken ct = default);
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

    /// <summary>
    /// Read-modify-write helper: appends <paramref name="text"/> to the order's current
    /// eshop remark, separated by a newline. If the order has no remark yet, the remark
    /// becomes <paramref name="text"/> verbatim (no leading separator).
    /// Equivalent to:
    ///   var current = await GetEshopRemarkAsync(orderCode, ct);
    ///   var updated = string.IsNullOrEmpty(current) ? text : $"{current}\n{text}";
    ///   await UpdateEshopRemarkAsync(orderCode, updated, ct);
    /// </summary>
    Task AppendEshopRemarkAsync(string orderCode, string text, CancellationToken ct = default);

    /// <summary>
    /// Returns every order currently in the given Shoptet status, across all pages
    /// (GET /api/orders?statusId={id}, itemsPerPage=50). Maps to the Application-layer
    /// <see cref="EshopOrderSummary"/> (code, externalCode, email, statusId).
    /// </summary>
    Task<List<EshopOrderSummary>> ListOrdersByStatusAsync(int statusId, CancellationToken ct = default);

    /// <summary>
    /// Orders currently in the given status created at or after <paramref name="createdFrom"/>, across all
    /// pages (GET /api/orders?statusId=&amp;creationTimeFrom=, itemsPerPage=50). Summaries carry the header
    /// total <see cref="EshopOrderSummary.PriceWithVat"/>.
    /// </summary>
    Task<IReadOnlyList<EshopOrderSummary>> ListOrdersByStatusAsync(
        int statusId, DateTimeOffset createdFrom, CancellationToken ct = default);

    /// <summary>
    /// Codes of every order that has an invoice created at or after <paramref name="createdFrom"/>
    /// (GET /api/invoices?creationTimeFrom=, all pages). Invalid (cancelled) invoices count too.
    /// </summary>
    Task<IReadOnlySet<string>> ListInvoicedOrderCodesAsync(DateTimeOffset createdFrom, CancellationToken ct = default);

    /// <summary>
    /// Whether any invoice (valid or not) references the order (GET /api/invoices?orderCode=).
    /// Checked right before issuing an invoice, so one created after the bulk listing is not duplicated.
    /// </summary>
    Task<bool> HasInvoiceAsync(string orderCode, CancellationToken ct = default);

    /// <summary>
    /// POST /api/orders/{code}/invoice with body {"data":{}} — Shoptet issues the invoice dated today
    /// (shoptet-api.md §3.17). Non-2xx throws HttpRequestException with StatusCode and Shoptet's error text.
    /// </summary>
    Task CreateInvoiceAsync(string orderCode, CancellationToken ct = default);

    /// <summary>
    /// Transitions the order to the configured "packed" state
    /// (Shoptet "Zabaleno", id 52 by default). Called by the Balení screen
    /// after a successful scan + shipment creation.
    /// </summary>
    Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);

    /// <summary>
    /// Orders (code + current status id) created at or after <paramref name="createdFrom"/> that contain a
    /// line with exactly <paramref name="productCode"/> (GET /api/orders?productCode=&amp;creationTimeFrom=, all pages).
    /// The filter matches items[] line codes only, not set components (shoptet-api.md §3.8).
    /// </summary>
    Task<IReadOnlyList<EshopOrderSummary>> ListOrdersContainingProductAsync(
        string productCode, DateTimeOffset createdFrom, CancellationToken ct = default);

    /// <summary>
    /// GET /api/orders/{code} mapped to status + lines. Returns null when the order does not exist (404).
    /// </summary>
    Task<EshopOrderDetail?> GetOrderDetailAsync(string orderCode, CancellationToken ct = default);

    /// <summary>
    /// POST /api/orders/{code}/item. Adding a product-set line makes Shoptet expand its components
    /// and deduct their stock (shoptet-api.md §3.17). Non-2xx throws HttpRequestException with StatusCode.
    /// </summary>
    Task AddOrderItemAsync(string orderCode, NewEshopOrderItem item, CancellationToken ct = default);

    /// <summary>
    /// DELETE /api/orders/{code}/item/{itemId}. Shoptet returns the line's stock (shoptet-api.md §3.17).
    /// Non-2xx throws HttpRequestException with StatusCode.
    /// </summary>
    Task DeleteOrderItemAsync(string orderCode, long itemId, CancellationToken ct = default);
}
