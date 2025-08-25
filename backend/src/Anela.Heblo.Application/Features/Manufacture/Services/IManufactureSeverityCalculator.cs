using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IManufactureSeverityCalculator
{
    ManufacturingStockSeverity CalculateSeverity(
        CatalogAggregate catalogAggregate,
        double dailySalesRate,
        double overstockPercentage);
}