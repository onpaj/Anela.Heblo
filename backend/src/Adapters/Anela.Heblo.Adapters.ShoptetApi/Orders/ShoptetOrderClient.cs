using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Domain.Features.ShoptetOrders;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

public class ShoptetOrderClient : IShoptetOrderClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ShoptetOrderClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// List all orders whose externalCode starts with the given prefix.
    /// Because externalCode is not returned by the list endpoint, this method fetches
    /// the single-order detail for each candidate. Pass emailFilter to narrow candidates
    /// before issuing detail requests (the list endpoint does include email).
    /// Paginates automatically (API max is 50 items per page).
    /// </summary>
    public async Task<List<OrderSummary>> ListByExternalCodePrefixAsync(
        string prefix,
        string? emailFilter = null,
        CancellationToken ct = default)
    {
        var result = new List<OrderSummary>();
        var page = 1;

        while (true)
        {
            var response = await _http.GetAsync($"/api/orders?page={page}&itemsPerPage=50", ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
            if (data == null)
                break;

            IEnumerable<OrderSummary> candidates = data.Data.Orders;

            if (emailFilter != null)
                candidates = candidates.Where(o => string.Equals(o.Email, emailFilter, StringComparison.OrdinalIgnoreCase));

            foreach (var summary in candidates)
            {
                var detail = await GetOrderDetailAsync(summary.Code, ct);
                if (detail.ExternalCode?.StartsWith(prefix, StringComparison.Ordinal) == true)
                    result.Add(detail);
            }

            if (page >= data.Data.Paginator.PageCount)
                break;

            page++;
        }

        return result;
    }

    /// <summary>
    /// Get a single order by its Shoptet order code. Returns the full order summary
    /// including externalCode, which is not available in the list endpoint.
    /// </summary>
    public async Task<OrderSummary> GetOrderDetailAsync(string code, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/orders/{code}", ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions, ct);
        return data!.Data.Order;
    }

    /// <summary>
    /// Get the current status ID for an order.
    /// </summary>
    public async Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default)
    {
        var detail = await GetOrderDetailAsync(orderCode, ct);
        return detail.Status.Id;
    }

    /// <summary>
    /// Create a new order. Returns the created order code.
    /// </summary>
    public async Task<string> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        // Shoptet REST API requires the body wrapped in {"data": {...}}
        var envelope = new { data = request };
        var response = await _http.PostAsJsonAsync("/api/orders", envelope, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions, ct);
        return result!.Data.Order.Code;
    }

    /// <summary>
    /// Update the status of an existing order.
    /// </summary>
    public async Task UpdateStatusAsync(string orderCode, int statusId, CancellationToken ct = default)
    {
        var body = new UpdateStatusRequest
        {
            Data = new UpdateStatusData { StatusId = statusId },
        };

        var response = await _http.PatchAsJsonAsync($"/api/orders/{orderCode}/status", body, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"PATCH /api/orders/{orderCode}/status returned {(int)response.StatusCode}: {errorBody}");
        }
    }

    /// <summary>
    /// Set the internal note on an existing order.
    /// </summary>
    public async Task SetInternalNoteAsync(string orderCode, string note, CancellationToken ct = default)
    {
        var body = new UpdateNotesRequest
        {
            Data = new UpdateNotesData { InternalNote = note },
        };

        var response = await _http.PatchAsJsonAsync($"/api/orders/{orderCode}/notes", body, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"PATCH /api/orders/{orderCode}/notes returned {(int)response.StatusCode}: {errorBody}");
        }
    }

    /// <summary>
    /// Delete an order by its code.
    /// </summary>
    public async Task DeleteOrderAsync(string orderCode, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/orders/{orderCode}", ct);
        response.EnsureSuccessStatusCode();
    }
}
