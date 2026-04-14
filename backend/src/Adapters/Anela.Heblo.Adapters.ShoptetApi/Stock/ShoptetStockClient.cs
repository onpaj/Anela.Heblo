using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Stock.Model;
using Anela.Heblo.Application.Features.Catalog.Stock;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Stock;

public class ShoptetStockClient : IShoptetStockClient
{
    private readonly HttpClient _http;
    private readonly IOptions<Orders.ShoptetApiSettings> _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ShoptetStockClient(HttpClient http, IOptions<Orders.ShoptetApiSettings> settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task UpdateStockAsync(string productCode, double amountChange, CancellationToken ct = default)
    {
        var stockId = _settings.Value.StockId;

        var body = new UpdateStockRequest
        {
            Data = [new UpdateStockItem { ProductCode = productCode, AmountChange = amountChange }],
        };

        var response = await _http.PatchAsJsonAsync($"/api/stocks/{stockId}/movements", body, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"PATCH /api/stocks/{stockId}/movements returned {(int)response.StatusCode}: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<UpdateStockResponse>(JsonOptions, ct);
        if (result?.Errors is { Count: > 0 })
        {
            var error = result.Errors[0];
            throw new HttpRequestException(
                $"Shoptet stock update failed for {productCode}: [{error.ErrorCode}] {error.Message}");
        }
    }
}
