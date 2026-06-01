namespace Anela.Heblo.Domain.Features.Analytics;

/// <summary>
/// Analytics-specific repository with streaming capabilities.
/// Prevents memory issues by avoiding loading all data at once.
/// </summary>
public interface IAnalyticsRepository
{
    /// <summary>
    /// Streams products with sales data to avoid memory overload
    /// </summary>
    IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime fromDate,
        DateTime toDate,
        AnalyticsProductType[] productTypes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregated margin data directly from repository (optimized query)
    /// </summary>
    Task<Dictionary<string, decimal>> GetGroupMarginTotalsAsync(
        DateTime fromDate,
        DateTime toDate,
        AnalyticsProductType[] productTypes,
        ProductGroupingMode groupingMode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed product analysis data for a specific product
    /// </summary>
    Task<AnalyticsProduct?> GetProductAnalysisDataAsync(
        string productId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets daily invoice import statistics for monitoring purposes
    /// </summary>
    Task<List<DailyInvoiceCount>> GetInvoiceImportStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets daily bank statement import statistics for monitoring purposes
    /// </summary>
    Task<List<DailyBankStatementStatistics>> GetBankStatementImportStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        BankStatementDateType dateType,
        CancellationToken cancellationToken = default);
}
