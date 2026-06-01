using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetBankStatementImportStatistics;
using Anela.Heblo.Domain.Features.Analytics;

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
        AnalyticsProductType[] productTypes,
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