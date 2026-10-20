using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public class MarginCalculationService : IMarginCalculationService
{
    private readonly IMaterialCostProvider _materialCostProvider;
    private readonly IFlatManufactureCostProvider _flatManufactureCostProvider;
    private readonly IOverheadCostProvider _overheadCostProvider;
    private readonly ISalesCostProvider _salesCostProvider;
    private readonly ILogger<MarginCalculationService> _logger;

    public MarginCalculationService(
        IMaterialCostProvider materialCostProvider,
        IFlatManufactureCostProvider flatManufactureCostProvider,
        IOverheadCostProvider overheadCostProvider,
        ISalesCostProvider salesCostProvider,
        ILogger<MarginCalculationService> logger)
    {
        _materialCostProvider = materialCostProvider;
        _flatManufactureCostProvider = flatManufactureCostProvider;
        _overheadCostProvider = overheadCostProvider;
        _salesCostProvider = salesCostProvider;
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
        var materialCosts = await _materialCostProvider.GetCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);
        var flatManufactureCosts = await _flatManufactureCostProvider.GetCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);
        var overheadCosts = await _overheadCostProvider.GetCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);
        var salesCosts = await _salesCostProvider.GetCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);

        return new CostData
        {
            MaterialCosts = materialCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>()),
            FlatManufactureCosts = flatManufactureCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>()),
            OverheadCosts = overheadCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>()),
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
            var m1Cost = GetCostForMonth(costData.FlatManufactureCosts, currentMonth);
            var m2Cost = GetCostForMonth(costData.SalesCosts, currentMonth);
            var m3Cost = GetCostForMonth(costData.OverheadCosts, currentMonth);

            // Each level adds its own cost layer to the running total, so CostTotal is cumulative
            // and CostLevel is that level's own increment.
            var m0 = MarginLevel.Create(sellingPrice, m0Cost, m0Cost);
            var m1 = MarginLevel.Create(sellingPrice, m0Cost + m1Cost, m1Cost);
            var m2 = MarginLevel.Create(sellingPrice, m0Cost + m1Cost + m2Cost, m2Cost);
            var m3 = MarginLevel.Create(sellingPrice, m0Cost + m1Cost + m2Cost + m3Cost, m3Cost);

            // Create margin data for this month
            var marginData = new MarginData
            {
                M0 = m0,
                M1 = m1,
                M2 = m2,
                M3 = m3
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
        public List<MonthlyCost> OverheadCosts { get; set; } = new();
        public List<MonthlyCost> SalesCosts { get; set; } = new();
    }
}