using Anela.Heblo.Application.Common;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog;

public class ManufactureCostCalculationService : IManufactureCostCalculationService
{
    private readonly ILedgerService _ledgerService;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<DataSourceOptions> _dataSourceOptions;
    private readonly ILogger<ManufactureCostCalculationService> _logger;

    public ManufactureCostCalculationService(
        ILedgerService ledgerService,
        TimeProvider timeProvider,
        IOptions<DataSourceOptions> dataSourceOptions,
        ILogger<ManufactureCostCalculationService> logger)
    {
        _ledgerService = ledgerService;
        _timeProvider = timeProvider;
        _dataSourceOptions = dataSourceOptions;
        _logger = logger;
    }

    public async Task<Dictionary<string, List<ManufactureCost>>> CalculateManufactureCostHistoryAsync(
        List<CatalogAggregate> products,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, List<ManufactureCost>>();

        // Get date range for last 13 months, values from last months are not accurate yet
        var endDate = _timeProvider.GetUtcNow().Date;
        var startDate = endDate.AddDays(-1 * _dataSourceOptions.Value.ManufactureCostHistoryDays);

        _logger.LogDebug("Calculating manufacture cost history from {StartDate} to {EndDate}", startDate, endDate);

        // Get direct costs from VYROBA department
        var directCosts = await _ledgerService.GetDirectCosts(startDate, endDate, "VYROBA", cancellationToken);
        var personalCosts = await _ledgerService.GetPersonalCosts(startDate, endDate, cancellationToken: cancellationToken);

        var totalCosts = directCosts.Concat(personalCosts.Select(s => new CostStatistics()
        {
            Date = s.Date,
            Cost = s.Cost / 2,
            Department = s.Department
        }));

        // Group direct costs by month
        var monthlyCosts = totalCosts
            .GroupBy(c => new { c.Date.Year, c.Date.Month })
            .ToDictionary(
                g => $"{g.Key.Year:D4}-{g.Key.Month:D2}",
                g => g.Sum(c => c.Cost)
            );

        _logger.LogDebug("Found costs for {MonthCount} months", monthlyCosts.Count);

        // Get all manufacture history from products and group by month
        var allManufactureHistory = products
            .Where(p => p.ManufactureDifficulty > 0) // Only products with difficulty
            .SelectMany(p => p.ManufactureHistory.Select(m => new { Product = p, ManufactureRecord = m }))
            .Where(x => x.ManufactureRecord.Date >= startDate && x.ManufactureRecord.Date <= endDate)
            .ToList();

        var monthlyManufactureData = allManufactureHistory
            .GroupBy(x => new { x.ManufactureRecord.Date.Year, x.ManufactureRecord.Date.Month })
            .ToDictionary(
                g => $"{g.Key.Year:D4}-{g.Key.Month:D2}",
                g => g.ToList()
            );

        _logger.LogDebug("Found manufacture history for {MonthCount} months with {TotalRecords} records",
            monthlyManufactureData.Count, allManufactureHistory.Count);

        // For each month, calculate weighted costs
        foreach (var monthKey in monthlyCosts.Keys)
        {
            if (!monthlyManufactureData.TryGetValue(monthKey, out var monthManufactures))
                continue;

            var totalMonthlyCost = monthlyCosts[monthKey];

            // Calculate total weighted production for the month
            var totalWeightedProduction = 0.0;
            foreach (var item in monthManufactures)
            {
                if(item.Product.ManufactureDifficulty.HasValue)
                    totalWeightedProduction += item.ManufactureRecord.Amount * item.Product.ManufactureDifficulty.Value;
            }

            // Skip if no weighted production
            if (totalWeightedProduction <= 0)
            {
                _logger.LogWarning("No weighted production found for month {MonthKey}", monthKey);
                continue;
            }

            // Calculate cost per weighted unit
            var costPerWeightedUnit = totalMonthlyCost / (decimal)totalWeightedProduction;

            _logger.LogDebug("Month {MonthKey}: Total cost {TotalCost}, Weighted production {WeightedProduction}, Cost per unit {CostPerUnit}",
                monthKey, totalMonthlyCost, totalWeightedProduction, costPerWeightedUnit);

            // Group by product and calculate weighted average material cost per product per month
            var productGroups = monthManufactures.GroupBy(x => x.Product.ProductCode);

            foreach (var productGroup in productGroups)
            {
                var productCode = productGroup.Key;
                var productManufactures = productGroup.ToList();
                var product = productGroup.First().Product;

                // Calculate weighted average MaterialCost for this product this month
                var totalProductAmount = (decimal)productManufactures.Sum(x => x.ManufactureRecord.Amount);
                var weightedMaterialCostSum = productManufactures.Sum(x => x.ManufactureRecord.PricePerPiece * (decimal)x.ManufactureRecord.Amount);
                var materialCostPerPiece = totalProductAmount > 0 ? weightedMaterialCostSum / totalProductAmount : 0;

                // Calculate handling cost for this product this month
                var totalProductWeightedProduction = productManufactures.Sum(x => (decimal)x.ManufactureRecord.Amount * (decimal)product.ManufactureDifficulty);
                var productHandlingCost = costPerWeightedUnit * totalProductWeightedProduction;
                var handlingCostPerPiece = totalProductAmount > 0 ? productHandlingCost / totalProductAmount : 0;

                if (!result.ContainsKey(productCode))
                {
                    result[productCode] = new List<ManufactureCost>();
                }

                // Parse month key to get year and month
                var parts = monthKey.Split('-');
                var year = int.Parse(parts[0]);
                var month = int.Parse(parts[1]);
                var monthDate = new DateTime(year, month, 1);
                
                var manufactureCost = new ManufactureCost
                {
                    Date = monthDate,
                    HandlingCost = handlingCostPerPiece,
                    MaterialCostFromReceiptDocument = materialCostPerPiece,
                };

                result[productCode].Add(manufactureCost);

                _logger.LogDebug("Product {ProductCode} - Month {MonthKey}: Material cost {MaterialCost}, Handling cost {HandlingCost}",
                    productCode, monthKey, materialCostPerPiece, handlingCostPerPiece);
            }
        }

        _logger.LogInformation("Calculated manufacture cost history for {ProductCount} products", result.Count);
        return result;
    }
}