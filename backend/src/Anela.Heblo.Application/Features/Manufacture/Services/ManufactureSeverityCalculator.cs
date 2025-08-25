using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ManufactureSeverityCalculator : IManufactureSeverityCalculator
{
    private const double CriticalOverstockThreshold = 100.0;
    private readonly ILogger<ManufactureSeverityCalculator> _logger;

    public ManufactureSeverityCalculator(ILogger<ManufactureSeverityCalculator> logger)
    {
        _logger = logger;
    }

    public ManufacturingStockSeverity CalculateSeverity(
        CatalogAggregate catalogItem,
        double dailySalesRate,
        double stockDaysAvailable)
    {
        // Gray - Missing optimalStockDaysSetup configuration (Unconfigured)
        if (!IsConfiguredForAnalysis(catalogItem))
        {
            _logger.LogDebug("Item {Code} marked as Unconfigured: OptimalStockDaysSetup = {Setup}",
                catalogItem.ProductCode, catalogItem.Properties.OptimalStockDaysSetup);
            return ManufacturingStockSeverity.Unconfigured;
        }

        var overstockPercentage = CalculateOverstockPercentage(stockDaysAvailable, catalogItem.Properties.OptimalStockDaysSetup);

        // Red - Overstock < 100% (Critical) - only for products with optimalStockDaysSetup > 0
        if (overstockPercentage < CriticalOverstockThreshold && dailySalesRate > 0)
        {
            _logger.LogDebug("Item {Code} marked as Critical: overstock {Percentage}% < {Threshold}%, dailySales {Rate}",
                catalogItem.ProductCode, overstockPercentage, CriticalOverstockThreshold, dailySalesRate);
            return ManufacturingStockSeverity.Critical;
        }

        // Orange - Below minimum stock (Major) - only for products with minStockSetup > 0
        if (catalogItem.Properties.StockMinSetup > 0 && catalogItem.Stock.Available < catalogItem.Properties.StockMinSetup)
        {
            _logger.LogDebug("Item {Code} marked as Major: current stock {Current} < min stock setup {Min}",
                catalogItem.ProductCode, catalogItem.Stock.Available, catalogItem.Properties.StockMinSetup);
            return ManufacturingStockSeverity.Major;
        }

        // Green - All conditions OK (Adequate)
        _logger.LogDebug("Item {Code} marked as Adequate: overstock {Percentage}%, above min stock",
            catalogItem.ProductCode, overstockPercentage);
        return ManufacturingStockSeverity.Adequate;
    }

    public double CalculateOverstockPercentage(double stockDaysAvailable, int optimalStockDaysSetup)
    {
        if (optimalStockDaysSetup <= 0)
        {
            _logger.LogDebug("Cannot calculate overstock percentage: optimalStockDaysSetup is {Setup}", optimalStockDaysSetup);
            return 0;
        }

        if (double.IsInfinity(stockDaysAvailable))
        {
            _logger.LogDebug("Stock days available is infinite, returning 0% overstock");
            return 0;
        }

        var percentage = (stockDaysAvailable / optimalStockDaysSetup) * 100;

        _logger.LogDebug("Calculated overstock percentage: {Percentage}% (stock days: {StockDays}, optimal: {Optimal})",
            percentage, stockDaysAvailable, optimalStockDaysSetup);

        return percentage;
    }

    public bool IsConfiguredForAnalysis(CatalogAggregate catalogItem)
    {
        var isConfigured = catalogItem.Properties.OptimalStockDaysSetup > 0;

        _logger.LogDebug("Configuration check for {Code}: OptimalStockDaysSetup = {Setup}, configured = {IsConfigured}",
            catalogItem.ProductCode, catalogItem.Properties.OptimalStockDaysSetup, isConfigured);

        return isConfigured;
    }
}