using Anela.Heblo.Application.Features.FinancialOverview;

namespace Anela.Heblo.Application.Features.FinancialOverview.Services;

public interface IFinancialAnalysisService
{
    /// <summary>
    /// Gets financial overview data, preferably from cache.
    /// When <paramref name="excludedDepartments"/> is null or empty and <paramref name="includeCurrentMonth"/> is false,
    /// the cached path is used. Otherwise, a real-time calculation is performed.
    /// </summary>
    Task<GetFinancialOverviewResponse> GetFinancialOverviewAsync(
        int months,
        bool includeStockData,
        IReadOnlyList<string>? excludedDepartments = null,
        bool includeCurrentMonth = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes cached financial data for specified date range
    /// </summary>
    Task RefreshFinancialDataAsync(
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the cache status for monitoring
    /// </summary>
    FinancialAnalysisCacheStatus GetCacheStatus();

    /// <summary>
    /// Gets year-over-year financial comparison data, aligning each year's partial month
    /// to the same cutoff day for a fair comparison.
    /// </summary>
    Task<GetFinancialComparisonResponse> GetFinancialComparisonAsync(
        int years,
        bool includeStockData,
        IReadOnlyList<string>? excludedDepartments,
        bool includePartialMonth,
        CancellationToken cancellationToken = default);
}