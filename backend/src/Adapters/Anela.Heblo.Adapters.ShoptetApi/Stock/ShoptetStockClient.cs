using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Products.Model;
using Anela.Heblo.Adapters.ShoptetApi.Stock.Model;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Stock;

public class ShoptetStockClient : IEshopStockClient
{
    private readonly HttpClient _http;
    private readonly IOptions<Orders.ShoptetApiSettings> _settings;
    private readonly IOptions<ShoptetStockClientOptions> _stockClientOptions;
    private readonly ILogger<ShoptetStockClient> _logger;

    private const int SuppliesItemsPerPage = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ShoptetStockClient(
        HttpClient http,
        IOptions<Orders.ShoptetApiSettings> settings,
        IOptions<ShoptetStockClientOptions> stockClientOptions,
        ILogger<ShoptetStockClient> logger)
    {
        _http = http;
        _settings = settings;
        _stockClientOptions = stockClientOptions;
        _logger = logger;
    }

    public async Task<List<EshopStock>> ListAsync(CancellationToken cancellationToken)
    {
        const string OperationName = "ShoptetStockClient.ListAsync";

        var stockId = _settings.Value.StockId;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = new List<EshopStock>();
            var pageCount = 1;
            for (var page = 1; page <= pageCount; page++)
            {
                var response = await _http.GetFromJsonAsync<GetSuppliesResponse>(
                    $"/api/stocks/{stockId}/supplies?itemsPerPage={SuppliesItemsPerPage}&page={page}",
                    JsonOptions,
                    cancellationToken);

                if (response?.Data is null)
                {
                    throw new InvalidOperationException($"Shoptet supplies page {page} has no data.");
                }

                result.AddRange(response.Data.Supplies.Select(MapSupply));
                pageCount = Math.Max(pageCount, response.Data.Paginator?.PageCount ?? 1);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Operation {Operation} failed. StockId={StockId} ExceptionType={ExceptionType} Message={Message} InnerExceptionType={InnerExceptionType} InnerMessage={InnerMessage} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                OperationName,
                stockId,
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
                "Operation {Operation} failed. StockId={StockId} ExceptionType={ExceptionType} Message={Message} InnerExceptionType={InnerExceptionType} InnerMessage={InnerMessage} ElapsedMs={ElapsedMs}",
                OperationName,
                stockId,
                ex.GetType().FullName,
                ex.Message,
                ex.InnerException?.GetType().FullName,
                ex.InnerException?.Message,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private static EshopStock MapSupply(SupplyItem item) => new()
    {
        Code = item.Code,
        Stock = decimal.TryParse(item.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) ? amount : 0m,
        Location = item.Location ?? string.Empty,
    };

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

        var isAmountKnown = double.TryParse(item.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount);
        if (!isAmountKnown)
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
            IsAmountKnown = isAmountKnown,
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
}
