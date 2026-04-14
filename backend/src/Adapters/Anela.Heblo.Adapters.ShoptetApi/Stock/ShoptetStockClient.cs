using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Stock.Model;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Stock;

public class ShoptetStockClient : IEshopStockClient
{
    private readonly HttpClient _http;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<Orders.ShoptetApiSettings> _settings;
    private readonly IOptions<ShoptetStockClientOptions> _stockClientOptions;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ShoptetStockClient(
        HttpClient http,
        IHttpClientFactory httpClientFactory,
        IOptions<Orders.ShoptetApiSettings> settings,
        IOptions<ShoptetStockClientOptions> stockClientOptions)
    {
        _http = http;
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _stockClientOptions = stockClientOptions;
    }

    public async Task<List<EshopStock>> ListAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        List<EshopStock> stockDataList = new List<EshopStock>();
        using (HttpResponseMessage response = await client.GetAsync(_stockClientOptions.Value.Url, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            using (Stream csvStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            using (StreamReader reader = new StreamReader(csvStream, Encoding.GetEncoding("windows-1250")))
            using (CsvReader csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
            {
                csv.Context.RegisterClassMap<StockDataMap>();
                stockDataList = csv.GetRecords<EshopStock>().ToList();
            }
        }

        return stockDataList;
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

    public async Task<EshopStockSupply?> GetSupplyAsync(string productCode, CancellationToken ct = default)
    {
        var stockId = _settings.Value.StockId;
        var response = await _http.GetFromJsonAsync<GetSuppliesResponse>(
            $"/api/stocks/{stockId}/supplies?code={Uri.EscapeDataString(productCode)}",
            JsonOptions,
            ct);

        var item = response?.Data?.Supplies.FirstOrDefault();
        if (item is null)
        {
            return null;
        }

        return new EshopStockSupply
        {
            Code = item.Code,
            Amount = double.TryParse(item.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var a) ? a : 0,
            Claim = double.TryParse(item.Claim, NumberStyles.Any, CultureInfo.InvariantCulture, out var c) ? c : 0,
        };
    }

    public async Task SetRealStockAsync(string productCode, double realStock, CancellationToken ct = default)
    {
        var stockId = _settings.Value.StockId;
        var body = new UpdateStockRequest
        {
            Data = [new UpdateStockItem { ProductCode = productCode, RealStock = realStock }],
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
                $"Shoptet stock set failed for {productCode}: [{error.ErrorCode}] {error.Message}");
        }
    }

    private class StockDataMap : ClassMap<EshopStock>
    {
        public StockDataMap()
        {
            Map(m => m.Code).Index(0);
            Map(m => m.PairCode).Index(1);
            Map(m => m.Name).Index(2);
            Map(m => m.Stock).Index(25).TypeConverterOption.NullValues(string.Empty).Default(0m);
            Map(m => m.NameSuffix).Index(15);
            Map(m => m.Location).Index(26);
            Map(m => m.DefaultImage).Index(3);
            Map(m => m.Image).Index(4);
            Map(m => m.Weight).Index(27);
            Map(m => m.Height).Index(28);
            Map(m => m.Depth).Index(29);
            Map(m => m.Width).Index(30);
            Map(m => m.AtypicalShipping).Index(31);
        }
    }
}
