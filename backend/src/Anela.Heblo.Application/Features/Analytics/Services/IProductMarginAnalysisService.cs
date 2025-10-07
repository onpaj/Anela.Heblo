using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface IProductMarginAnalysisService
{
    (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow);
    Task<Dictionary<string, decimal>> CalculateGroupTotalMarginAsync(List<CatalogAggregate> products, DateTime fromDate, DateTime toDate, ProductGroupingMode groupingMode, CancellationToken cancellationToken = default);
    string GetGroupKey(CatalogAggregate product, ProductGroupingMode groupingMode);
    string GetGroupDisplayName(string groupKey, ProductGroupingMode groupingMode, List<CatalogAggregate> products);
    Task<decimal> CalculateMaterialCostsAsync(CatalogAggregate product, CancellationToken cancellationToken = default);
    Task<decimal> CalculateLaborCostsAsync(CatalogAggregate product, CancellationToken cancellationToken = default);
}