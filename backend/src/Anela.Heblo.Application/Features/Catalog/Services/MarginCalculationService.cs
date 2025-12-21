using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public class MarginCalculationService : IMarginCalculationService
{
    private readonly IMaterialCostSource _materialCostSource;
    private readonly IFlatManufactureCostSource _flatManufactureCostSource;
    private readonly IDirectManufactureCostSource _directManufactureCostSource;
    private readonly ISalesCostSource _salesCostSource;
    private readonly ILogger<MarginCalculationService> _logger;

    public MarginCalculationService(
        IMaterialCostSource materialCostSource,
        IFlatManufactureCostSource flatManufactureCostSource,
        IDirectManufactureCostSource directManufactureCostSource,
        ISalesCostSource salesCostSource,
        ILogger<MarginCalculationService> logger)
    {
        _materialCostSource = materialCostSource;
        _flatManufactureCostSource = flatManufactureCostSource;
        _directManufactureCostSource = directManufactureCostSource;
        _salesCostSource = salesCostSource;
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
        var materialCosts = await _materialCostSource.GetCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);
        var flatManufactureCosts = await _flatManufactureCostSource.GetCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);
        var directManufactureCosts = await _directManufactureCostSource.GetCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);
        var salesCosts = await _salesCostSource.GetCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);

        return new CostData
        {
            MaterialCosts = materialCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>()),
            FlatManufactureCosts = flatManufactureCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>()),
            DirectManufactureCosts = directManufactureCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>()),
            SalesCosts = salesCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>()),
        };
    }


    private MonthlyMarginHistory CalculateMarginHistoryFromData(decimal sellingPrice, CostData costData, DateOnly dateFrom, DateOnly dateTo)
    {
        var result = new MonthlyMarginHistory
        {
            LastUpdated = DateTime.UtcNow
        };

        // Generate all months in range
        var currentMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (currentMonth <= endMonth)
        {
            // Find costs for this month
            var m0Cost = GetCostForMonth(costData.MaterialCosts, currentMonth);
            var m1ACost = GetCostForMonth(costData.FlatManufactureCosts, currentMonth);
            var m1BCost = GetCostForMonth(costData.DirectManufactureCosts, currentMonth);
            var m2Cost = GetCostForMonth(costData.SalesCosts, currentMonth);

            // Calculate margin levels
            // M0: Material only
            var m0 = MarginLevel.Create(sellingPrice, m0Cost, m0Cost);

            // M1_A: Material + Flat manufacturing (independent from M1_B)
            var m1A = MarginLevel.Create(sellingPrice, m0Cost + m1ACost, m1ACost);

            // M1_B: Material + Direct manufacturing (independent from M1_A)
            var m1B = MarginLevel.Create(sellingPrice, m0Cost + m1BCost, m1BCost);

            // M2: All costs combined (Material + Flat + Direct + Sales/Marketing)
            var m2 = MarginLevel.Create(sellingPrice, m0Cost + m1ACost + m1BCost + m2Cost, m2Cost);

            // Create margin data for this month
            var marginData = new MarginData
            {
                M0 = m0,
                M1_A = m1A,
                M1_B = m1B,
                M2 = m2
            };

            result.MonthlyData[currentMonth] = marginData;

            currentMonth = currentMonth.AddMonths(1);
        }

        return result;
    }

    private static decimal GetCostForMonth(List<MonthlyCost> costs, DateTime month)
    {
        var monthlyCost = costs.FirstOrDefault(c => c.Month.Year == month.Year && c.Month.Month == month.Month);
        return monthlyCost?.Cost ?? 0m;
    }



    private class CostData
    {
        public List<MonthlyCost> MaterialCosts { get; set; } = new();
        public List<MonthlyCost> FlatManufactureCosts { get; set; } = new();
        public List<MonthlyCost> DirectManufactureCosts { get; set; } = new();
        public List<MonthlyCost> SalesCosts { get; set; } = new();
    }
}