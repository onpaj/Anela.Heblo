using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Persistence.Features.Analytics;

/// <summary>
/// 🔒 PERFORMANCE FIX: Analytics repository with streaming capabilities
/// Prevents memory overload by delegating to IAnalyticsProductSource
/// </summary>
public sealed class AnalyticsRepository : IAnalyticsRepository
{
    private readonly IAnalyticsProductSource _productSource;
    private readonly IInvoiceImportStatisticsSource _invoiceImportStatisticsSource;
    private readonly IBankStatementStatisticsSource _bankStatementStatisticsSource;

    public AnalyticsRepository(
        IAnalyticsProductSource productSource,
        IInvoiceImportStatisticsSource invoiceImportStatisticsSource,
        IBankStatementStatisticsSource bankStatementStatisticsSource)
    {
        _productSource = productSource;
        _invoiceImportStatisticsSource = invoiceImportStatisticsSource;
        _bankStatementStatisticsSource = bankStatementStatisticsSource;
    }

    /// <summary>
    /// Streams products with sales to avoid memory overload
    /// Delegates to the product source implementation
    /// </summary>
    public IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime fromDate,
        DateTime toDate,
        AnalyticsProductType[] productTypes,
        CancellationToken cancellationToken = default)
    {
        return _productSource.StreamProductsWithSalesAsync(fromDate, toDate, productTypes, cancellationToken);
    }

    public Task<AnalyticsProduct?> GetProductAnalysisDataAsync(
        string productId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        return _productSource.GetProductAnalysisDataAsync(productId, fromDate, toDate, cancellationToken);
    }

    /// <summary>
    /// Gets daily invoice import statistics for monitoring purposes
    /// </summary>
    public async Task<List<DailyInvoiceCount>> GetInvoiceImportStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default)
    {
        var counts = await _invoiceImportStatisticsSource.GetDailyCountsAsync(
            startDate, endDate, dateType, cancellationToken);
        return counts.ToList();
    }

    /// <summary>
    /// Gets daily bank statement import statistics for monitoring purposes
    /// </summary>
    public async Task<List<DailyBankStatementStatistics>> GetBankStatementImportStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        BankStatementDateType dateType,
        CancellationToken cancellationToken = default)
    {
        var stats = await _bankStatementStatisticsSource.GetDailyStatisticsAsync(
            startDate, endDate, dateType, cancellationToken);
        return stats.ToList();
    }
}
