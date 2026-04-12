using System.Net.Http.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Stock.Model;
using Anela.Heblo.Application.Features.Catalog.Stock;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Stock;

public class ShoptetStockClient : IShoptetStockClient
{
    private readonly HttpClient _http;
    private readonly IOptions<ShoptetApiSettings> _settings;

    public ShoptetStockClient(HttpClient http, IOptions<ShoptetApiSettings> settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task UpdateStockAsync(string productCode, double amountChange, CancellationToken ct = default)
    {
        var stockId = _settings.Value.StockId;
        var url = $"/api/stocks/{stockId}/movements";

        var body = new UpdateStockRequest
        {
            Data = new List<UpdateStockItem>
            {
                new UpdateStockItem
                {
                    ProductCode = productCode,
                    AmountChange = amountChange,
                },
            },
        };

        var response = await _http.PatchAsJsonAsync(url, body, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<UpdateStockResponse>(cancellationToken: ct);
        if (result?.Errors is { Count: > 0 })
        {
            var firstError = result.Errors[0];
            throw new InvalidOperationException(
                $"Shoptet stock update failed for product {firstError.Instance}: [{firstError.ErrorCode}] {firstError.Message}");
        }
    }
}
