using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ShoptetApiExpeditionClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ShoptetApiExpeditionClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ExpeditionOrderListResponse> GetOrdersByStatusAsync(int statusId, int page, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/orders?status={statusId}&page={page}&itemsPerPage=50", ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<ExpeditionOrderListResponse>(JsonOptions, ct);
        return data ?? new ExpeditionOrderListResponse();
    }

    public async Task<ExpeditionOrderDetail> GetOrderDetailAsync(string code, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/orders/{code}", ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<ExpeditionOrderDetailResponse>(JsonOptions, ct);
        return data!.Data.Order;
    }

    public async Task UpdateOrderStatusAsync(string code, int statusId, CancellationToken ct = default)
    {
        var body = new { data = new { statusId } };

        var response = await _http.PatchAsJsonAsync($"/api/orders/{code}/status", body, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"PATCH /api/orders/{code}/status returned {(int)response.StatusCode}: {errorBody}");
        }
    }
}
