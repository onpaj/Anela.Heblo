using System.Net.Http.Json;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Application.Features.ShoptetOrders;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

/// <summary>
/// Order-invoice operations for the missing-invoice job: which completed orders exist, which orders
/// already have an invoice, and issuing an invoice for an order (shoptet-api.md §3.17, §10).
/// </summary>
public partial class ShoptetOrderClient
{
    private const int InvoiceListPageSize = 1000;

    public async Task<IReadOnlyList<EshopOrderSummary>> ListOrdersByStatusAsync(
        int statusId, DateTimeOffset createdFrom, CancellationToken ct = default)
    {
        var from = Uri.EscapeDataString(createdFrom.ToString(CreationTimeFormat, Invariant));
        var result = new List<EshopOrderSummary>();

        for (var page = 1; ; page++)
        {
            var path = $"/api/orders?statusId={statusId.ToString(Invariant)}&creationTimeFrom={from}&page={page}&itemsPerPage={OrderListPageSize}";
            var response = await _http.GetAsync(path, ct);
            await EnsureShoptetSuccessAsync(response, "GET", path, ct);

            var data = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
            if (data == null)
                break;

            result.AddRange(data.Data.Orders.Select(o => new EshopOrderSummary
            {
                Code = o.Code,
                StatusId = o.Status.Id,
                CreationTime = ParseShoptetTime(o.CreationTime),
                PriceWithVat = o.Price?.WithVat,
            }));
            if (page >= data.Data.Paginator.PageCount)
                break;
        }

        return result;
    }

    public async Task<IReadOnlySet<string>> ListInvoicedOrderCodesAsync(
        DateTimeOffset createdFrom, CancellationToken ct = default)
    {
        var from = Uri.EscapeDataString(createdFrom.ToString(CreationTimeFormat, Invariant));
        var result = new HashSet<string>(StringComparer.Ordinal);

        for (var page = 1; ; page++)
        {
            var path = $"/api/invoices?creationTimeFrom={from}&page={page}&itemsPerPage={InvoiceListPageSize}";
            var response = await _http.GetAsync(path, ct);
            await EnsureShoptetSuccessAsync(response, "GET", path, ct);

            // A silently empty set would make every order look uninvoiced — fail instead.
            var data = await response.Content.ReadFromJsonAsync<InvoiceListResponse>(JsonOptions, ct)
                ?? throw new InvalidOperationException($"Shoptet GET {path} returned an empty body.");

            foreach (var invoice in data.Data.Invoices)
            {
                if (!string.IsNullOrEmpty(invoice.OrderCode))
                    result.Add(invoice.OrderCode);
            }
            if (page >= data.Data.Paginator.PageCount)
                break;
        }

        return result;
    }

    public async Task CreateInvoiceAsync(string orderCode, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderCode);

        // An empty body is rejected with 422 "Missing request data" — the envelope must be {"data":{}}.
        var path = $"/api/orders/{Uri.EscapeDataString(orderCode)}/invoice";
        var response = await _http.PostAsJsonAsync(path, new { data = new { } }, JsonOptions, ct);
        await EnsureShoptetSuccessAsync(response, "POST", path, ct);
    }
}
