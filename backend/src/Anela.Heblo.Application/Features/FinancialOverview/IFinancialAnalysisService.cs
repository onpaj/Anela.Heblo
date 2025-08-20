using Anela.Heblo.Application.Features.FinancialOverview.Model;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public interface IFinancialAnalysisService
{
    /// <summary>
    /// Gets financial overview data, preferably from cache
    /// </summary>
    Task<GetFinancialOverviewResponse> GetFinancialOverviewAsync(
        int months,
        bool includeStockData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes cached financial data for specified date range
    /// </summary>
    Task RefreshFinancialDataAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the cache status for monitoring
    /// </summary>
    FinancialAnalysisCacheStatus GetCacheStatus();
}