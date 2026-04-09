using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Model;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal sealed class FlexiManufactureTemplateService : IFlexiManufactureTemplateService
{
    private readonly IBoMClient _bomClient;
    private readonly IErpStockClient _stockClient;
    private readonly TimeProvider _timeProvider;

    public FlexiManufactureTemplateService(
        IBoMClient bomClient,
        IErpStockClient stockClient,
        TimeProvider timeProvider)
    {
        _bomClient = bomClient ?? throw new ArgumentNullException(nameof(bomClient));
        _stockClient = stockClient ?? throw new ArgumentNullException(nameof(stockClient));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ManufactureTemplate?> GetManufactureTemplateAsync(string productCode, CancellationToken cancellationToken = default)
    {
        var bom = await _bomClient.GetAsync(productCode, cancellationToken);

        var header = bom.SingleOrDefault(s => s.Level == 1);
        if (header == null)
            return null;

        var ingredients = bom.Where(w => w.Level != 1);

        // Get stock data to determine HasLots for each ingredient
        var stockDate = _timeProvider.GetLocalNow().DateTime;
        var allStockData = new List<ErpStock>();

        // Load stock from all warehouses to get HasLots information
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
                    HasExpiration = false // This information is not available from BoM, set to false as default
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

            // Return UNDEFINED if no ProductTypeId is available
            if (!productTypeId.HasValue)
            {
                return ProductType.UNDEFINED;
            }

            // Check if the value is a valid ProductType enum value
            if (Enum.IsDefined(typeof(ProductType), productTypeId.Value))
            {
                return (ProductType)productTypeId.Value;
            }

            // Return UNDEFINED for unknown enum values
            return ProductType.UNDEFINED;
        }
        catch
        {
            // Return UNDEFINED for any exceptions
            return ProductType.UNDEFINED;
        }
    }
}
