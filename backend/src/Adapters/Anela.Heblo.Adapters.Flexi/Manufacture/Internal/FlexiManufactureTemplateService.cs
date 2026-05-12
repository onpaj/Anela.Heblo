using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Model;
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

    public Task<ManufactureTemplate?> GetManufactureTemplateAsync(string productCode, CancellationToken cancellationToken = default)
    {
        return _cache.GetOrFetchAsync(productCode, ct => FetchAsync(productCode, ct), cancellationToken);
    }

    private async Task<ManufactureTemplate?> FetchAsync(string productCode, CancellationToken cancellationToken)
    {
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

        var header = bom.SingleOrDefault(s => s.Level == 1);
        if (header == null)
            return null;

        var ingredients = bom.Where(w => w.Level != 1);

        // Get stock data to determine HasLots for each ingredient (sequential — will be parallelised in Task 10)
        var stockDate = _timeProvider.GetLocalNow().DateTime;
        var allStockData = new List<ErpStock>();

        var warehouseIds = new[]
        {
            FlexiStockClient.MaterialWarehouseId,
            FlexiStockClient.SemiProductsWarehouseId,
            FlexiStockClient.ProductsWarehouseId
        };

        foreach (var warehouseId in warehouseIds)
        {
            var stockItems = await _stockClient.StockToDateAsync(stockDate, warehouseId, cancellationToken);
            allStockData.AddRange(stockItems);
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
                var stockItem = allStockData.FirstOrDefault(stock => stock.ProductCode == code);

                return new Ingredient()
                {
                    TemplateId = s.Id,
                    ProductCode = code,
                    ProductName = s.IngredientFullName,
                    Amount = s.Amount,
                    ProductType = ResolveProductType(s),
                    HasLots = stockItem?.HasLots ?? false,
                    HasExpiration = false
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
}
