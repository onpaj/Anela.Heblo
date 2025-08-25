using Anela.Heblo.Domain.Features.FinancialOverview;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.FinancialOverview;

/// <summary>
/// Test placeholder implementation for IStockValueService used in Test and Automation environments.
/// This service provides predictable empty stock change data for consistent testing scenarios.
/// 
/// <para>
/// <strong>Usage Context:</strong>
/// - Automatically injected in Test and Automation environments via FinancialOverviewModule
/// - Prevents external ERP dependencies during automated testing
/// - Ensures deterministic behavior for financial analysis tests
/// </para>
/// 
/// <para>
/// <strong>DO NOT USE in Production or Development environments.</strong>
/// Real environments use StockValueService with actual ERP integration.
/// </para>
/// 
/// <para>
/// <strong>Implementation Note:</strong>
/// Returns empty stock changes to support financial analysis calculations 
/// without requiring external ERP stock data connections.
/// </para>
/// </summary>
public class PlaceholderStockValueService : IStockValueService
{
    private readonly ILogger<PlaceholderStockValueService> _logger;

    public PlaceholderStockValueService(ILogger<PlaceholderStockValueService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns an empty list of stock changes for testing purposes.
    /// This placeholder implementation ensures financial analysis tests can run 
    /// without external ERP stock data dependencies.
    /// </summary>
    /// <param name="startDate">Start date for stock change analysis (ignored in placeholder)</param>
    /// <param name="endDate">End date for stock change analysis (ignored in placeholder)</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Empty list of MonthlyStockChange for predictable test behavior</returns>
    public Task<IReadOnlyList<MonthlyStockChange>> GetStockValueChangesAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("PlaceholderStockValueService: Returning empty stock changes for test environment. Date range: {StartDate} to {EndDate}",
            startDate, endDate);

        // Return empty list for test environments - real implementation uses ERP integration
        var emptyChanges = new List<MonthlyStockChange>();
        return Task.FromResult<IReadOnlyList<MonthlyStockChange>>(emptyChanges);
    }
}