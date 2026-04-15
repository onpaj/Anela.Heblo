namespace Anela.Heblo.Application.Features.FinancialOverview.Services;

public interface IFinancialAnalysisService
{
    /// <summary>
    /// Gets financial overview data, preferably from cache.
    /// When <paramref name="excludedDepartments"/> is null or empty, the cached path is used.
    /// When populated, a real-time calculation is performed with department filtering applied.
    /// </summary>
    Task<GetFinancialOverviewResponse> GetFinancialOverviewAsync(
        int months,
        bool includeStockData,
        IReadOnlyList<string>? excludedDepartments = null,
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
}