using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Model;
using System.Diagnostics;
using System.Net;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal sealed class FlexiManufactureTemplateService : IFlexiManufactureTemplateService
{
    private readonly IBoMClient _bomClient;
    private readonly IErpStockClient _stockClient;
    private readonly TimeProvider _timeProvider;
    private readonly IManufactureTemplateCache _cache;
    private readonly ITelemetryService _telemetry;
    private readonly ILogger<FlexiManufactureTemplateService> _logger;

    public FlexiManufactureTemplateService(
        IBoMClient bomClient,
        IErpStockClient stockClient,
        TimeProvider timeProvider,
        IManufactureTemplateCache cache,
        ITelemetryService telemetry,
        ILogger<FlexiManufactureTemplateService> logger)
    {
        _bomClient = bomClient ?? throw new ArgumentNullException(nameof(bomClient));
        _stockClient = stockClient ?? throw new ArgumentNullException(nameof(stockClient));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ManufactureTemplate?> GetManufactureTemplateAsync(string productCode, CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var fetchTimings = new FetchTimings();

        var template = await _cache.GetOrFetchAsync(
            productCode,
            ct => FetchAsync(productCode, ct, fetchTimings),
            cancellationToken);

        totalStopwatch.Stop();
        EmitTelemetry(productCode, fetchTimings, totalStopwatch.ElapsedMilliseconds, template);
        return template;
    }

    public void InvalidateTemplate(string productCode)
    {
        _cache.Invalidate(productCode);
        _logger.LogDebug("Manufacture template invalidated via service for {ProductCode}", productCode);
    }

    private async Task<ManufactureTemplate?> FetchAsync(string productCode, CancellationToken cancellationToken, FetchTimings timings)
    {
        timings.FetchInvoked = true;
        var bomStopwatch = Stopwatch.StartNew();
        IEnumerable<BoMItemFlexiDto> bom;
        try
        {
            bom = await _bomClient.GetAsync(productCode, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotImplemented)
        {
            _logger.LogError(ex,
                "FlexiBee kusovnik returned 501 NotImplemented while fetching BoM template — " +
                "endpoint may be disabled or unsupported on this instance. ProductCode: {ProductCode}",
                productCode);
            throw;
        }
        bomStopwatch.Stop();
        timings.BomMs = bomStopwatch.ElapsedMilliseconds;

        var header = bom.SingleOrDefault(s => s.Level == 1);
        if (header == null)
            return null;

        var ingredients = bom.Where(w => w.Level != 1);

        var stockDate = _timeProvider.GetLocalNow().DateTime;

        var stockStopwatch = Stopwatch.StartNew();
        var materialStockTask = _stockClient.StockToDateAsync(stockDate, FlexiStockClient.MaterialWarehouseId, cancellationToken);
        var semiProductsStockTask = _stockClient.StockToDateAsync(stockDate, FlexiStockClient.SemiProductsWarehouseId, cancellationToken);
        var productsStockTask = _stockClient.StockToDateAsync(stockDate, FlexiStockClient.ProductsWarehouseId, cancellationToken);

        await Task.WhenAll(materialStockTask, semiProductsStockTask, productsStockTask);
        stockStopwatch.Stop();
        timings.StockMs = stockStopwatch.ElapsedMilliseconds;

        // Narrow the three full snapshots to a single HasLots dictionary (FR-4).
        // Larger DTOs go out of scope immediately after this block.
        var hasLotsByProductCode = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var snapshot in new[] { materialStockTask.Result, semiProductsStockTask.Result, productsStockTask.Result })
        {
            foreach (var stockItem in snapshot)
            {
                hasLotsByProductCode[stockItem.ProductCode] = stockItem.HasLots;
            }
        }

        var template = new ManufactureTemplate()
        {
            TemplateId = header.Id,
            ProductCode = header.IngredientCode.RemoveCodePrefix(),
            ProductName = header.IngredientFullName,
            Amount = header.Amount,
            OriginalAmount = header.Amount,
            Ingredients = ingredients.Select(s =>
            {
                var code = s.IngredientCode.RemoveCodePrefix();

                return new Ingredient()
                {
                    TemplateId = s.Id,
                    ProductCode = code,
                    ProductName = s.IngredientFullName,
                    Amount = s.Amount,
                    ProductType = ResolveProductType(s),
                    HasLots = hasLotsByProductCode.TryGetValue(code, out var hasLots) && hasLots,
                    HasExpiration = false,
                    Order = s.Order,
                    PhaseLabel = s.NameC?.Trim() is { Length: 1 } v && v[0] is >= 'A' and <= 'Z' ? v : null,
                };
            }).ToList(),
        };

        if (ingredients.Any(a => a.Ingredient.Any(b => b.ProductTypeId == (int)ProductType.SemiProduct)))
            template.ManufactureType = ManufactureType.MultiPhase;
        else
            template.ManufactureType = ManufactureType.SinglePhase;

        return template;
    }

    private static ProductType ResolveProductType(BoMItemFlexiDto boMItemFlexiDto)
    {
        try
        {
            var productTypeId = boMItemFlexiDto.Ingredient?.FirstOrDefault()?.ProductTypeId;
            if (!productTypeId.HasValue)
            {
                return ProductType.UNDEFINED;
            }
            if (Enum.IsDefined(typeof(ProductType), productTypeId.Value))
            {
                return (ProductType)productTypeId.Value;
            }
            return ProductType.UNDEFINED;
        }
        catch
        {
            return ProductType.UNDEFINED;
        }
    }

    private sealed class FetchTimings
    {
        public bool FetchInvoked { get; set; }
        public long BomMs { get; set; }
        public long StockMs { get; set; }
    }

    private void EmitTelemetry(string productCode, FetchTimings timings, long totalMs, ManufactureTemplate? template)
    {
        try
        {
            var properties = new Dictionary<string, string>
            {
                ["product_code"] = productCode,
                ["cache_hit"] = (!timings.FetchInvoked).ToString().ToLowerInvariant(),
                ["ingredient_count"] = (template?.Ingredients.Count ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            var metrics = new Dictionary<string, double>
            {
                ["bom_duration_ms"] = timings.BomMs,
                ["stock_duration_ms"] = timings.StockMs,
                ["total_duration_ms"] = totalMs
            };
            _telemetry.TrackBusinessEvent("manufacture_template_fetched", properties, metrics);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to emit manufacture_template_fetched telemetry for {ProductCode}", productCode);
        }
    }
}
