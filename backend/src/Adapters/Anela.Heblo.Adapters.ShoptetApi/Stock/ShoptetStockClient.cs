using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Stock.Model;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Stock;

public class ShoptetStockClient : IEshopStockClient
{
    private readonly HttpClient _http;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<Orders.ShoptetApiSettings> _settings;
    private readonly IOptions<ShoptetStockClientOptions> _stockClientOptions;
    private readonly ILogger<ShoptetStockClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ShoptetStockClient(
        HttpClient http,
        IHttpClientFactory httpClientFactory,
        IOptions<Orders.ShoptetApiSettings> settings,
        IOptions<ShoptetStockClientOptions> stockClientOptions,
        ILogger<ShoptetStockClient> logger)
    {
        _http = http;
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _stockClientOptions = stockClientOptions;
        _logger = logger;
    }

    public async Task<List<EshopStock>> ListAsync(CancellationToken cancellationToken)
    {
        const string OperationName = "ShoptetStockClient.ListAsync";

        var url = _stockClientOptions.Value.Url;
        var redactedUrl = RedactToken(url);
        var client = _httpClientFactory.CreateClient("ShoptetStockCsv");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Operation {Operation} failed. Url={Url} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                    OperationName, redactedUrl, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                response.EnsureSuccessStatusCode();
            }

            using Stream csvStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using StreamReader reader = new StreamReader(csvStream, Encoding.GetEncoding("windows-1250"));
            using CsvReader csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" });
            csv.Context.RegisterClassMap<StockDataMap>();
            return csv.GetRecords<EshopStock>().ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Operation {Operation} failed. Url={Url} ExceptionType={ExceptionType} Message={Message} InnerExceptionType={InnerExceptionType} InnerMessage={InnerMessage} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                OperationName,
                redactedUrl,
                ex.GetType().FullName,
                ex.Message,
                ex.InnerException?.GetType().FullName,
                ex.InnerException?.Message,
                (int?)ex.StatusCode,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Operation {Operation} failed. Url={Url} ExceptionType={ExceptionType} Message={Message} InnerExceptionType={InnerExceptionType} InnerMessage={InnerMessage} ElapsedMs={ElapsedMs}",
                OperationName,
                redactedUrl,
                ex.GetType().FullName,
                ex.Message,
                ex.InnerException?.GetType().FullName,
                ex.InnerException?.Message,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
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

        if (!double.TryParse(item.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
        {
            _logger.LogWarning(
                "Could not parse Amount '{Amount}' for product {Code} from Shoptet supply response. Defaulting to 0.",
                item.Amount, item.Code);
        }

        if (!double.TryParse(item.Claim, NumberStyles.Any, CultureInfo.InvariantCulture, out var claim))
        {
            _logger.LogWarning(
                "Could not parse Claim '{Claim}' for product {Code} from Shoptet supply response. Defaulting to 0.",
                item.Claim, item.Code);
        }

        return new EshopStockSupply
        {
            Code = item.Code,
            Amount = amount,
            Claim = claim,
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

    internal static string RedactToken(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url;
        }

        var queryIndex = url.IndexOf('?');
        if (queryIndex < 0)
        {
            return url;
        }

        var prefix = url.Substring(0, queryIndex + 1);
        var query = url.Substring(queryIndex + 1);
        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < pairs.Length; i++)
        {
            var equalsIndex = pairs[i].IndexOf('=');
            var key = equalsIndex < 0 ? pairs[i] : pairs[i].Substring(0, equalsIndex);
            if (key.Equals("token", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("hash", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("key", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("apiToken", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("access_token", StringComparison.OrdinalIgnoreCase))
            {
                pairs[i] = key + "=***";
            }
        }

        return prefix + string.Join('&', pairs);
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
