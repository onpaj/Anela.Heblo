using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public class SalesCostCalculationService : ISalesCostCalculationService
{
    private readonly ILogger<SalesCostCalculationService> _logger;

    public SalesCostCalculationService(ILogger<SalesCostCalculationService> logger)
    {
        _logger = logger;
    }

    public async Task<Dictionary<string, List<SalesCost>>> CalculateSalesCostHistoryAsync(
        List<CatalogAggregate> products,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Calculating sales cost history for {ProductCount} products", products.Count);

        var result = new Dictionary<string, List<SalesCost>>();

        foreach (var product in products)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Stub implementation - generate zero costs for now
            // In full implementation, this would calculate marketing and storage costs
            var salesCostHistory = GenerateStubSalesCostHistory();

            result[product.ProductCode] = salesCostHistory;
        }

        _logger.LogInformation("Completed sales cost calculation for {ProductCount} products", products.Count);
        return result;
    }

    private List<SalesCost> GenerateStubSalesCostHistory()
    {
        var salesCosts = new List<SalesCost>();
        var currentMonth = DateTime.UtcNow.Date.AddDays(-DateTime.UtcNow.Day + 1); // First day of current month

        // Generate 13 months of history (current + 12 previous)
        for (int i = 0; i < 13; i++)
        {
            var month = currentMonth.AddMonths(-i);
            salesCosts.Add(new SalesCost
            {
                Date = month,
                MarketingCost = 0, // Stub value - will be replaced with real calculation
                StorageCost = 0    // Stub value - will be replaced with real calculation
            });
        }

        return salesCosts.OrderBy(sc => sc.Date).ToList();
    }
}