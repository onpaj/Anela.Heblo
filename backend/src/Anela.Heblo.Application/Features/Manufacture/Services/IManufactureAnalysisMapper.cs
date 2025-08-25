using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IManufactureAnalysisMapper
{
    ManufacturingStockItemDto MapToDto(
        CatalogAggregate item,
        DateTime fromDate,
        DateTime toDate,
        ManufacturingStockSeverity severity);

    (double salesInPeriod, double dailySalesRate, double stockDaysAvailable, double overstockPercentage) CalculateStockMetrics(
        CatalogAggregate item,
        DateTime fromDate,
        DateTime toDate);
}