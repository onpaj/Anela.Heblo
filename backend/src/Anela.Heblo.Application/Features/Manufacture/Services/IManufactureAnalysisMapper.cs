using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IManufactureAnalysisMapper
{
    /// <summary>
    /// Maps a catalog aggregate and analysis results to a manufacturing stock item DTO.
    /// </summary>
    /// <param name="catalogItem">Catalog aggregate with product data</param>
    /// <param name="severity">Calculated severity level</param>
    /// <param name="dailySalesRate">Calculated daily sales rate</param>
    /// <param name="salesInPeriod">Total sales in the analysis period</param>
    /// <param name="stockDaysAvailable">Number of days stock will last</param>
    /// <param name="overstockPercentage">Overstock percentage relative to optimal</param>
    /// <param name="isInProduction">Whether the item is in active production</param>
    /// <returns>Manufacturing stock item DTO</returns>
    ManufacturingStockItemDto MapToDto(
        CatalogAggregate catalogItem,
        ManufacturingStockSeverity severity,
        double dailySalesRate,
        double salesInPeriod,
        double stockDaysAvailable,
        double overstockPercentage,
        bool isInProduction);
}