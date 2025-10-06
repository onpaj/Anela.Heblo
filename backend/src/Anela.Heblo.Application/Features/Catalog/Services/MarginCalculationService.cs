using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Services;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public class MarginCalculationService : IMarginCalculationService
{
    private const int AverageCostHistoryMonthsCount = 12;

    private readonly IManufactureCostCalculationService _manufactureCostService;
    private readonly ISalesCostCalculationService _salesCostService;
    private readonly ILogger<MarginCalculationService> _logger;

    public MarginCalculationService(
        IManufactureCostCalculationService manufactureCostService,
        ISalesCostCalculationService salesCostService,
        ILogger<MarginCalculationService> logger)
    {
        _manufactureCostService = manufactureCostService;
        _salesCostService = salesCostService;
        _logger = logger;
    }

    public async Task<ProductMarginResult> CalculateAllMarginLevelsAsync(
        CatalogAggregate product,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sellingPrice = product.PriceWithoutVat ?? 0;
            if (sellingPrice <= 0)
            {
                _logger.LogDebug("Product {ProductCode} has no valid selling price", product.ProductCode);
                return ProductMarginResult.Zero;
            }

            // Get current costs from historical data
            var costBreakdown = await CalculateCostBreakdownAsync(product, cancellationToken);

            // Calculate current month margins
            var m0 = MarginLevel.Create(sellingPrice, costBreakdown.M0Cost);
            var m1 = MarginLevel.Create(sellingPrice, costBreakdown.M1Cost);
            var m2 = MarginLevel.Create(sellingPrice, costBreakdown.M2Cost);
            var m3 = MarginLevel.Create(sellingPrice, costBreakdown.M3Cost);

            // Calculate 12-month averages
            var monthlyHistory = await CalculateMonthlyMarginHistoryAsync(product, AverageCostHistoryMonthsCount, cancellationToken);

            return new ProductMarginResult(
                m0, m1, m2, m3,
                costBreakdown,
                monthlyHistory.YearlyAverages.M0Average,
                monthlyHistory.YearlyAverages.M1Average,
                monthlyHistory.YearlyAverages.M2Average,
                monthlyHistory.YearlyAverages.M3Average
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating margin levels for product {ProductCode}", product.ProductCode);
            return ProductMarginResult.Zero;
        }
    }

    public async Task<MonthlyMarginHistory> CalculateMonthlyMarginHistoryAsync(
        CatalogAggregate product,
        int monthsBack = 13,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var monthlyData = new List<MonthlyMarginData>();
            var sellingPrice = product.PriceWithoutVat ?? 0;

            if (sellingPrice <= 0)
            {
                return new MonthlyMarginHistory();
            }

            // Get monthly cost history data
            var manufactureCostHistory = product.ManufactureCostHistory ?? new List<ManufactureCost>();
            var salesCostHistory = await GetSalesCostHistoryForProduct(product, cancellationToken);

            var currentMonth = DateTime.UtcNow.Date.AddDays(-DateTime.UtcNow.Day + 1); // First day of current month

            // Calculate margins for each month
            for (int i = 0; i < monthsBack; i++)
            {
                var month = currentMonth.AddMonths(-i);
                var monthlyMargin = CalculateMarginForMonth(month, sellingPrice, manufactureCostHistory, salesCostHistory);
                monthlyData.Add(monthlyMargin);
            }

            // Calculate averages (excluding zero values)
            var averages = CalculateMarginAverages(monthlyData);

            return new MonthlyMarginHistory
            {
                MonthlyData = monthlyData.OrderBy(m => m.Month).ToList(),
                YearlyAverages = averages
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating monthly margin history for product {ProductCode}", product.ProductCode);
            return new MonthlyMarginHistory();
        }
    }

    private async Task<CostBreakdown> CalculateCostBreakdownAsync(CatalogAggregate product, CancellationToken cancellationToken)
    {
        // Material cost - average from last 6 months of manufacture cost history
        var materialCost = CalculateAverageMaterialCostFromHistory(product.ManufactureCostHistory);

        // Manufacturing cost - average handling cost from manufacture cost history
        var manufacturingCost = CalculateAverageHandlingCostFromHistory(product.ManufactureCostHistory);

        // Sales cost - get from sales cost calculation service
        var salesCostHistory = await GetSalesCostHistoryForProduct(product, cancellationToken);
        var salesCost = CalculateAverageSalesCost(salesCostHistory);

        // Overhead cost - stub implementation for now
        var overheadCost = 0m; // Will be implemented when overhead cost calculation is available

        return new CostBreakdown(materialCost, manufacturingCost, salesCost, overheadCost);
    }

    private async Task<List<SalesCost>> GetSalesCostHistoryForProduct(CatalogAggregate product, CancellationToken cancellationToken)
    {
        var salesCostHistoryDict = await _salesCostService.CalculateSalesCostHistoryAsync(
            new List<CatalogAggregate> { product }, cancellationToken);

        return salesCostHistoryDict.TryGetValue(product.ProductCode, out var history)
            ? history
            : new List<SalesCost>();
    }

    private MonthlyMarginData CalculateMarginForMonth(
        DateTime month,
        decimal sellingPrice,
        List<ManufactureCost> manufactureCostHistory,
        List<SalesCost> salesCostHistory)
    {
        // Get costs for specific month
        var materialCost = GetMaterialCostForMonth(month, manufactureCostHistory);
        var manufacturingCost = GetManufacturingCostForMonth(month, manufactureCostHistory);
        var salesCost = GetSalesCostForMonth(month, salesCostHistory);
        var overheadCost = 0m; // Stub for now

        var costBreakdown = new CostBreakdown(materialCost, manufacturingCost, salesCost, overheadCost);

        return new MonthlyMarginData
        {
            Month = month,
            M0 = MarginLevel.Create(sellingPrice, costBreakdown.M0Cost),
            M1 = MarginLevel.Create(sellingPrice, costBreakdown.M1Cost),
            M2 = MarginLevel.Create(sellingPrice, costBreakdown.M2Cost),
            M3 = MarginLevel.Create(sellingPrice, costBreakdown.M3Cost),
            CostsForMonth = costBreakdown
        };
    }

    private MarginAverages CalculateMarginAverages(List<MonthlyMarginData> monthlyData)
    {
        var validData = monthlyData.Where(m => m.M0.Percentage > 0).ToList();

        if (!validData.Any())
        {
            return new MarginAverages();
        }

        return new MarginAverages
        {
            M0Average = new MarginLevel(
                validData.Average(m => m.M0.Percentage),
                validData.Average(m => m.M0.Amount),
                validData.Average(m => m.M0.CostBase)
            ),
            M1Average = new MarginLevel(
                validData.Average(m => m.M1.Percentage),
                validData.Average(m => m.M1.Amount),
                validData.Average(m => m.M1.CostBase)
            ),
            M2Average = new MarginLevel(
                validData.Average(m => m.M2.Percentage),
                validData.Average(m => m.M2.Amount),
                validData.Average(m => m.M2.CostBase)
            ),
            M3Average = new MarginLevel(
                validData.Average(m => m.M3.Percentage),
                validData.Average(m => m.M3.Amount),
                validData.Average(m => m.M3.CostBase)
            )
        };
    }

    private decimal CalculateAverageMaterialCostFromHistory(List<ManufactureCost> manufactureCostHistory)
    {
        if (manufactureCostHistory?.Any() != true)
            return 0;

        var lastSixNonZeroCosts = manufactureCostHistory
            .Where(c => c.MaterialCost > 0)
            .OrderByDescending(c => c.Date)
            .Take(6)
            .ToList();

        return lastSixNonZeroCosts.Any() ? lastSixNonZeroCosts.Average(c => c.MaterialCost) : 0;
    }

    private decimal CalculateAverageHandlingCostFromHistory(List<ManufactureCost> manufactureCostHistory)
    {
        if (manufactureCostHistory?.Any() != true)
            return 0;

        var lastSixNonZeroCosts = manufactureCostHistory
            .Where(c => c.HandlingCost > 0)
            .OrderByDescending(c => c.Date)
            .Take(6)
            .ToList();

        return lastSixNonZeroCosts.Any() ? lastSixNonZeroCosts.Average(c => c.HandlingCost) : 0;
    }

    private decimal CalculateAverageSalesCost(List<SalesCost> salesCostHistory)
    {
        if (salesCostHistory?.Any() != true)
            return 0;

        var lastSixNonZeroCosts = salesCostHistory
            .Where(c => c.Total > 0)
            .OrderByDescending(c => c.Date)
            .Take(6)
            .ToList();

        return lastSixNonZeroCosts.Any() ? lastSixNonZeroCosts.Average(c => c.Total) : 0;
    }

    private decimal GetMaterialCostForMonth(DateTime month, List<ManufactureCost> manufactureCostHistory)
    {
        var closestRecord = manufactureCostHistory
            ?.Where(c => c.Date <= month)
            ?.OrderByDescending(c => c.Date)
            ?.FirstOrDefault();

        return closestRecord?.MaterialCost ?? 0;
    }

    private decimal GetManufacturingCostForMonth(DateTime month, List<ManufactureCost> manufactureCostHistory)
    {
        var closestRecord = manufactureCostHistory
            ?.Where(c => c.Date <= month)
            ?.OrderByDescending(c => c.Date)
            ?.FirstOrDefault();

        return closestRecord?.HandlingCost ?? 0;
    }

    private decimal GetSalesCostForMonth(DateTime month, List<SalesCost> salesCostHistory)
    {
        var closestRecord = salesCostHistory
            ?.Where(c => c.Date <= month)
            ?.OrderByDescending(c => c.Date)
            ?.FirstOrDefault();

        return closestRecord?.Total ?? 0;
    }
}