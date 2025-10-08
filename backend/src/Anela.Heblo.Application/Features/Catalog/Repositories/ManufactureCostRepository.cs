using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Repositories;

public class ManufactureCostRepository : IManufactureCostRepository
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IManufactureCostCalculationService _manufactureCostCalculationService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ManufactureCostRepository> _logger;

    public ManufactureCostRepository(
        ICatalogRepository catalogRepository,
        IManufactureCostCalculationService manufactureCostCalculationService,
        TimeProvider timeProvider,
        ILogger<ManufactureCostRepository> logger)
    {
        _catalogRepository = catalogRepository;
        _manufactureCostCalculationService = manufactureCostCalculationService;
        _timeProvider = timeProvider;
        _logger = logger;
    }


    public async Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(List<string>? productCodes = null, DateOnly? dateFrom = null, DateOnly? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<CatalogAggregate> products;
        if (productCodes?.Count == 1)
        {
            var product = await _catalogRepository.GetByIdAsync(productCodes.Single(), cancellationToken);
            products = [product];
        }
        else
        {
            products = await _catalogRepository.GetAllAsync(cancellationToken);
            // Filter by product codes if specified
            if (productCodes != null && productCodes.Count > 0)
            {
                products = products.Where(p => p.ProductCode != null && productCodes.Contains(p.ProductCode)).ToList();
            }
        }

        return await GetCostsAsync(products, dateFrom, dateTo, cancellationToken);
    }

    private async Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(
        IEnumerable<CatalogAggregate> products,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date);
            var endDate = dateTo ?? now;
            var startDate = dateFrom ?? endDate.AddMonths(-12);

            _logger.LogDebug("Calculating manufacture costs from {StartDate} to {EndDate} for {ProductCount} products",
                startDate, endDate, products.Count());

            // Get manufacture cost history using existing service
            var manufactureCostHistory = await _manufactureCostCalculationService.CalculateManufactureCostHistoryAsync(products.ToList(), cancellationToken);

            var result = new Dictionary<string, List<MonthlyCost>>();

            foreach (var product in products)
            {
                if (string.IsNullOrEmpty(product.ProductCode))
                    continue;

                var monthlyCosts = new List<MonthlyCost>();

                if (manufactureCostHistory.TryGetValue(product.ProductCode, out var costHistory))
                {
                    // Filter by date range and convert to MonthlyCost
                    var filteredHistory = costHistory
                        .Where(c => DateOnly.FromDateTime(c.Date) >= startDate && DateOnly.FromDateTime(c.Date) <= endDate)
                        .OrderBy(c => c.Date);

                    foreach (var cost in filteredHistory)
                    {
                        // Use first day of month for consistency
                        var monthStart = new DateTime(cost.Date.Year, cost.Date.Month, 1);
                        monthlyCosts.Add(new MonthlyCost(monthStart, cost.HandlingCost));
                    }
                }

                // If we have some costs, add to result
                if (monthlyCosts.Count > 0)
                {
                    result[product.ProductCode] = monthlyCosts;
                }
            }

            _logger.LogInformation("Calculated manufacture costs for {ProductCount} products", result.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating manufacture costs");
            throw;
        }
    }
}