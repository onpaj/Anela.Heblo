using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface IProductMarginAnalysisService
{
    (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow);
    Dictionary<string, decimal> CalculateGroupTotalMargin(List<CatalogAggregate> products, DateTime fromDate, DateTime toDate, ProductGroupingMode groupingMode);
    string GetGroupKey(CatalogAggregate product, ProductGroupingMode groupingMode);
    string GetGroupDisplayName(string groupKey, ProductGroupingMode groupingMode, List<CatalogAggregate> products);
    decimal CalculateMaterialCosts(CatalogAggregate product);
    decimal CalculateLaborCosts(CatalogAggregate product);
}