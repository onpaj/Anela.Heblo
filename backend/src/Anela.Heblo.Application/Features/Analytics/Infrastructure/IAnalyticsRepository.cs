using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Domain;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Analytics.Infrastructure;

/// <summary>
/// 🔒 PERFORMANCE FIX: Analytics-specific repository with streaming capabilities
/// Prevents memory issues by avoiding loading all data at once
/// </summary>
public interface IAnalyticsRepository
{
    /// <summary>
    /// Streams products with sales data to avoid memory overload
    /// </summary>
    IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime fromDate,
        DateTime toDate,
        ProductType[] productTypes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregated margin data directly from repository (optimized query)
    /// </summary>
    Task<Dictionary<string, decimal>> GetGroupMarginTotalsAsync(
        DateTime fromDate,
        DateTime toDate,
        ProductType[] productTypes,
        ProductGroupingMode groupingMode,
        CancellationToken cancellationToken = default);
}