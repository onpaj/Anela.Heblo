using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ManufactureSeverityCalculator : IManufactureSeverityCalculator
{
    public ManufacturingStockSeverity CalculateSeverity(
        CatalogAggregate catalogAggregate,
        double dailySalesRate,
        double overstockPercentage)
    {
        // Gray - Missing optimalStockDaysSetup configuration (Unconfigured)
        // Product MUST have optimalStockDaysSetup to be categorized as Critical/Adequate
        if (catalogAggregate.Properties.OptimalStockDaysSetup <= 0)
        {
            return ManufacturingStockSeverity.Unconfigured;
        }

        // Red - Overstock < 100% (Critical) - only for products with optimalStockDaysSetup > 0
        if (overstockPercentage < 100 && dailySalesRate > 0)
        {
            return ManufacturingStockSeverity.Critical;
        }

        // Orange - Below minimum stock (Major) - only for products with minStockSetup > 0
        if (catalogAggregate.Properties.StockMinSetup > 0 && catalogAggregate.Stock.Available < catalogAggregate.Properties.StockMinSetup)
        {
            return ManufacturingStockSeverity.Major;
        }

        // Green - All conditions OK (Adequate)
        return ManufacturingStockSeverity.Adequate;
    }
}