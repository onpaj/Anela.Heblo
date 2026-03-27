using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

public class ShoptetOrderClient
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
    /// Find a single order by its exact externalCode.
    /// Returns null when not found.
    /// </summary>
    public async Task<OrderSummary?> FindByExternalCodeAsync(string externalCode, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(
            $"/api/orders?externalCode={Uri.EscapeDataString(externalCode)}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
        return result?.Data.Orders.FirstOrDefault(o => o.ExternalCode == externalCode);
    }

    /// <summary>
    /// List all orders whose externalCode starts with the given prefix.
    /// Paginates automatically.
    /// </summary>
    public async Task<List<OrderSummary>> ListByExternalCodePrefixAsync(string prefix, CancellationToken ct = default)
    {
        var result = new List<OrderSummary>();
        var page = 1;

        while (true)
        {
            var response = await _http.GetAsync($"/api/orders?page={page}&itemsPerPage=100", ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
            if (data == null)
                break;

            var matching = data.Data.Orders
                .Where(o => o.ExternalCode?.StartsWith(prefix, StringComparison.Ordinal) == true)
                .ToList();

            result.AddRange(matching);

            if (page >= data.Data.Paginator.PageCount)
                break;

            page++;
        }

        return result;
    }

    /// <summary>
    /// Create a new order. Returns the created order code.
    /// </summary>
    public async Task<string> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/orders", request, JsonOptions, ct);
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
            Data = new UpdateStatusData
            {
                Status = new UpdateStatusValue { Id = statusId },
            },
        };

        var response = await _http.PatchAsJsonAsync($"/api/orders/{orderCode}/status", body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
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
