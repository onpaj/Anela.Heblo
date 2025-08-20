using Anela.Heblo.Domain.Features.FinancialOverview;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.FinancialOverview;

/// <summary>
/// Placeholder implementation that returns empty stock changes
/// TODO: Replace with proper StockToDate integration
/// </summary>
public class PlaceholderStockValueService : IStockValueService
{
    private readonly ILogger<PlaceholderStockValueService> _logger;

    public PlaceholderStockValueService(ILogger<PlaceholderStockValueService> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<MonthlyStockChange>> GetStockValueChangesAsync(
        DateTime startDate, 
        DateTime endDate, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Placeholder stock value service called - returning empty changes from {StartDate} to {EndDate}", 
            startDate, endDate);
            
        // Return empty list for now - real implementation will be in adapter layer
        var emptyChanges = new List<MonthlyStockChange>();
        return Task.FromResult<IReadOnlyList<MonthlyStockChange>>(emptyChanges);
    }
}