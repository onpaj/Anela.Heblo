using Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IManufactureSeverityCalculator
{
    /// <summary>
    /// Determines the severity level for manufacturing stock based on business rules.
    /// </summary>
    /// <param name="catalogItem">Catalog item with stock and configuration data</param>
    /// <param name="dailySalesRate">Daily sales rate for the item</param>
    /// <param name="stockDaysAvailable">Number of days the current stock will last</param>
    /// <returns>Manufacturing stock severity level</returns>
    ManufacturingStockSeverity CalculateSeverity(
        CatalogAggregate catalogItem,
        double dailySalesRate,
        double stockDaysAvailable);

    /// <summary>
    /// Calculates the overstock percentage relative to optimal stock setup.
    /// </summary>
    /// <param name="stockDaysAvailable">Current stock days available</param>
    /// <param name="optimalStockDaysSetup">Configured optimal stock days</param>
    /// <returns>Overstock percentage (100% = exactly optimal, &lt;100% = below optimal)</returns>
    double CalculateOverstockPercentage(double stockDaysAvailable, int optimalStockDaysSetup);

    /// <summary>
    /// Determines if the item configuration is sufficient for severity calculation.
    /// </summary>
    /// <param name="catalogItem">Catalog item to check</param>
    /// <returns>True if item has necessary configuration for severity analysis</returns>
    bool IsConfiguredForAnalysis(CatalogAggregate catalogItem);
}