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
                        .OrderBy(c => c.Date)
                        .ToList();

                    if (filteredHistory.Count > 0)
                    {
                        // Fill missing months with last known data
                        monthlyCosts = FillMissingMonths(filteredHistory, startDate, endDate);
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

    private List<MonthlyCost> FillMissingMonths(List<ManufactureCost> costHistory, DateOnly startDate, DateOnly endDate)
    {
        var result = new List<MonthlyCost>();
        
        // Group by month and take the latest entry for each month (in case there are multiple entries per month)
        var monthlyData = costHistory
            .GroupBy(c => new DateTime(c.Date.Year, c.Date.Month, 1))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Date).First());

        // Generate all months in the range
        var currentMonth = new DateTime(startDate.Year, startDate.Month, 1);
        var lastMonth = new DateTime(endDate.Year, endDate.Month, 1);
        
        var lastKnownCost = 0m; // Default to 0 if no previous data exists
        var hasFoundFirstRecord = false;

        while (currentMonth <= lastMonth)
        {
            if (monthlyData.TryGetValue(currentMonth, out var monthData))
            {
                // We have actual data for this month
                lastKnownCost = monthData.HandlingCost;
                hasFoundFirstRecord = true;
                result.Add(new MonthlyCost(currentMonth, lastKnownCost));
            }
            else if (hasFoundFirstRecord)
            {
                // We don't have data for this month, but we've seen at least one record before
                // Use the last known cost
                result.Add(new MonthlyCost(currentMonth, lastKnownCost));
            }
            // If we haven't found the first record yet, skip this month (don't create records before first known data)

            currentMonth = currentMonth.AddMonths(1);
        }

        return result;
    }
}