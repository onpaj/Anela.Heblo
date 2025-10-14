using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public class MarginCalculationService : IMarginCalculationService
{
    private readonly IMaterialCostRepository _materialCostRepository;
    private readonly IManufactureCostRepository _manufactureCostRepository;
    private readonly ISalesCostCalculationService _salesCostCalculationService;
    private readonly IOverheadCostRepository _overheadCostRepository;
    private readonly ILogger<MarginCalculationService> _logger;

    public MarginCalculationService(
        IMaterialCostRepository materialCostRepository,
        IManufactureCostRepository manufactureCostRepository,
        ISalesCostCalculationService salesCostCalculationService,
        IOverheadCostRepository overheadCostRepository,
        ILogger<MarginCalculationService> logger)
    {
        _materialCostRepository = materialCostRepository;
        _manufactureCostRepository = manufactureCostRepository;
        _salesCostCalculationService = salesCostCalculationService;
        _overheadCostRepository = overheadCostRepository;
        _logger = logger;
    }

    public async Task<MonthlyMarginHistory> GetMarginAsync(
        CatalogAggregate product,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sellingPrice = product.PriceWithoutVat ?? 0;

            if (sellingPrice <= 0 || string.IsNullOrEmpty(product.ProductCode))
            {
                return new MonthlyMarginHistory();
            }

            // Load cost data for the specified period
            var costData = await LoadCostDataAsync(product, dateFrom, dateTo, cancellationToken);

            // Calculate monthly margin history using the loaded data
            var monthlyHistory = CalculateMarginHistoryFromData(sellingPrice, costData, dateFrom, dateTo);

            return monthlyHistory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating monthly margin history for product {ProductCode}", product.ProductCode);
            return new MonthlyMarginHistory();
        }
    }

    private async Task<CostData> LoadCostDataAsync(CatalogAggregate product, DateOnly dateFrom, DateOnly dateTo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(product.ProductCode))
        {
            return new CostData();
        }

        var productCodes = new List<string> { product.ProductCode };

        // Load all cost data once from repositories
        var materialCosts = await _materialCostRepository.GetCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);
        var manufactureCosts = await _manufactureCostRepository.GetCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);
        var salesCosts = await _salesCostCalculationService.GetCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);
        var overheadCosts = await _overheadCostRepository.GetCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);

        return new CostData
        {
            MaterialCosts = materialCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>()),
            ManufactureCosts = manufactureCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>()),
            SalesCosts = salesCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>()),
            OverheadCosts = overheadCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>())
        };
    }


    private MonthlyMarginHistory CalculateMarginHistoryFromData(decimal sellingPrice, CostData costData, DateOnly dateFrom, DateOnly dateTo)
    {
        var monthlyData = new List<MonthlyMarginData>();

        // Generate list of months in the date range
        var currentDate = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var endDateTime = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (currentDate <= endDateTime)
        {
            var monthlyMargin = CalculateMarginForMonth(currentDate, sellingPrice, costData);
            monthlyData.Add(monthlyMargin);
            currentDate = currentDate.AddMonths(1);
        }

        var averages = CalculateMarginAverages(monthlyData);

        return new MonthlyMarginHistory
        {
            MonthlyData = monthlyData,
            Averages = averages
        };
    }

    private MonthlyMarginData CalculateMarginForMonth(DateTime month, decimal sellingPrice, CostData costData)
    {
        // Get costs for specific month or closest available month
        var materialCost = GetCostForMonth(month, costData.MaterialCosts);
        var manufacturingCost = GetCostForMonth(month, costData.ManufactureCosts);
        var salesCost = GetCostForMonth(month, costData.SalesCosts);
        var overheadCost = GetCostForMonth(month, costData.OverheadCosts);

        var costBreakdown = new CostBreakdown(materialCost, manufacturingCost, salesCost, overheadCost);

        return new MonthlyMarginData
        {
            Month = month,
            M0 = MarginLevel.Create(sellingPrice, costBreakdown.M0CostTotal, materialCost),
            M1 = MarginLevel.Create(sellingPrice, costBreakdown.M1CostTotal, manufacturingCost),
            M2 = MarginLevel.Create(sellingPrice, costBreakdown.M2CostTotal, salesCost),
            M3 = MarginLevel.Create(sellingPrice, costBreakdown.M3CostTotal, overheadCost),
            CostsForMonth = costBreakdown
        };
    }

    private decimal GetCostForMonth(DateTime month, List<MonthlyCost> monthlyCosts)
    {
        // Find exact month match first
        var exactMatch = monthlyCosts.FirstOrDefault(c => c.Month.Year == month.Year && c.Month.Month == month.Month);
        if (exactMatch != null)
            return exactMatch.Cost;

        // Find closest previous month
        var closestPrevious = monthlyCosts
            .Where(c => c.Month <= month)
            .OrderByDescending(c => c.Month)
            .FirstOrDefault();

        return closestPrevious?.Cost ?? 0;
    }

    private MarginData CalculateMarginAverages(List<MonthlyMarginData> monthlyData)
    {
        var validData = monthlyData.Where(m => m.M0.Percentage > 0).ToList();

        if (!validData.Any())
        {
            return new MarginData();
        }

        return new MarginData
        {
            M0 = new MarginLevel(
                validData.Average(m => m.M0.Percentage),
                validData.Average(m => m.M0.Amount),
                validData.Average(m => m.M0.CostTotal),
                validData.Average(m => m.M0.CostLevel)
            ),
            M1 = new MarginLevel(
                validData.Average(m => m.M1.Percentage),
                validData.Average(m => m.M1.Amount),
                validData.Average(m => m.M1.CostTotal),
                validData.Average(m => m.M1.CostLevel)
            ),
            M2 = new MarginLevel(
                validData.Average(m => m.M2.Percentage),
                validData.Average(m => m.M2.Amount),
                validData.Average(m => m.M2.CostTotal),
                validData.Average(m => m.M2.CostLevel)
            ),
            M3 = new MarginLevel(
                validData.Average(m => m.M3.Percentage),
                validData.Average(m => m.M3.Amount),
                validData.Average(m => m.M3.CostTotal),
                validData.Average(m => m.M3.CostLevel)
            )
        };
    }

    private class CostData
    {
        public List<MonthlyCost> MaterialCosts { get; set; } = new();
        public List<MonthlyCost> ManufactureCosts { get; set; } = new();
        public List<MonthlyCost> SalesCosts { get; set; } = new();
        public List<MonthlyCost> OverheadCosts { get; set; } = new();
    }
}