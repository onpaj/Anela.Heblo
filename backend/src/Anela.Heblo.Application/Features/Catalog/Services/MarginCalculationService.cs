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
    private readonly IOverheadCostCalculationService _overheadCostCalculationService;
    private readonly ILogger<MarginCalculationService> _logger;

    public MarginCalculationService(
        IMaterialCostRepository materialCostRepository,
        IManufactureCostRepository manufactureCostRepository,
        ISalesCostCalculationService salesCostCalculationService,
        IOverheadCostCalculationService overheadCostCalculationService,
        ILogger<MarginCalculationService> logger)
    {
        _materialCostRepository = materialCostRepository;
        _manufactureCostRepository = manufactureCostRepository;
        _salesCostCalculationService = salesCostCalculationService;
        _overheadCostCalculationService = overheadCostCalculationService;
        _logger = logger;
    }

    public async Task<MonthlyMarginHistory> GetMarginAsync(
        CatalogAggregate product,
        IEnumerable<CatalogAggregate> allProducts,
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

            // Calculate company-wide produced CP
            var companyWideProducedCP = await CalculateCompanyWideProducedCPAsync(
                allProducts, dateFrom, dateTo, cancellationToken);

            // Load cost data for the specified period
            var costData = await LoadCostDataAsync(product, dateFrom, dateTo, cancellationToken);

            // Calculate monthly margin history using the loaded data
            var monthlyHistory = CalculateMarginHistoryFromData(
                product, sellingPrice, costData, companyWideProducedCP, dateFrom, dateTo);

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
        var overheadCosts = await _overheadCostCalculationService.GetCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);

        return new CostData
        {
            MaterialCosts = materialCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>()),
            ManufactureCosts = manufactureCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>()),
            SalesCosts = salesCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>()),
            OverheadCosts = overheadCosts.GetValueOrDefault(product.ProductCode, new List<MonthlyCost>())
        };
    }


    private MonthlyMarginHistory CalculateMarginHistoryFromData(
        CatalogAggregate product,
        decimal sellingPrice,
        CostData costData,
        Dictionary<DateTime, decimal> companyWideProducedCP,
        DateOnly dateFrom,
        DateOnly dateTo)
    {
        var monthlyData = new List<MonthlyMarginData>();

        // Get product complexity points
        var productCP = product.ManufactureDifficulty ?? 0;

        // Calculate M1_A and M1_B for all months
        var m1_A_ByMonth = CalculateM1_A_PerMonth(
            productCP, companyWideProducedCP, costData.ManufactureCosts, dateFrom, dateTo);

        var m1_B_ByMonth = CalculateM1_B_PerMonth(
            product, productCP, companyWideProducedCP, costData.ManufactureCosts);

        // Generate list of months in the date range
        var currentDate = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var endDateTime = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (currentDate <= endDateTime)
        {
            var monthlyMargin = CalculateMarginForMonth(
                currentDate, sellingPrice, costData, m1_A_ByMonth, m1_B_ByMonth);
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

    private MonthlyMarginData CalculateMarginForMonth(
        DateTime month,
        decimal sellingPrice,
        CostData costData,
        Dictionary<DateTime, decimal> m1_A_ByMonth,
        Dictionary<DateTime, decimal?> m1_B_ByMonth)
    {
        // Get costs for specific month or closest available month
        var materialCost = GetCostForMonth(month, costData.MaterialCosts);
        var salesCost = GetCostForMonth(month, costData.SalesCosts);
        var overheadCost = GetCostForMonth(month, costData.OverheadCosts);

        // Get M1_A and M1_B for this month
        var m1_A_Cost = m1_A_ByMonth.GetValueOrDefault(month, 0);
        var m1_B_Cost = m1_B_ByMonth.GetValueOrDefault(month, null);

        // Use M1_A for cost breakdown (cumulative totals)
        var costBreakdown = new CostBreakdown(materialCost, m1_A_Cost, salesCost, overheadCost);

        return new MonthlyMarginData
        {
            Month = month,
            M0 = MarginLevel.Create(sellingPrice, costBreakdown.M0CostTotal, materialCost),
            M1_A = MarginLevel.Create(sellingPrice, costBreakdown.M1CostTotal, m1_A_Cost),
            M1_B = m1_B_Cost.HasValue
                ? MarginLevel.Create(sellingPrice, costBreakdown.M0CostTotal + m1_B_Cost.Value, m1_B_Cost.Value)
                : null,
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

    private async Task<Dictionary<DateTime, decimal>> CalculateCompanyWideProducedCPAsync(
        IEnumerable<CatalogAggregate> allProducts,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken)
    {
        var producedCPByMonth = new Dictionary<DateTime, decimal>();

        foreach (var product in allProducts)
        {
            if (product.ManufactureHistory == null || !product.ManufactureHistory.Any())
                continue;

            // Get production records in date range
            var productionRecords = product.ManufactureHistory
                .Where(h => h.Date >= dateFrom.ToDateTime(TimeOnly.MinValue)
                         && h.Date <= dateTo.ToDateTime(TimeOnly.MinValue))
                .ToList();

            foreach (var record in productionRecords)
            {
                // Get CP valid at production date
                var complexityPoints = product.ManufactureDifficulty;
                if (!complexityPoints.HasValue)
                {
                    _logger.LogWarning(
                        "Product {ProductCode} has no ManufactureDifficulty, skipping production record from {Date}",
                        product.ProductCode, record.Date);
                    continue;
                }

                // Calculate produced CP for this record
                var producedCP = Convert.ToDecimal(record.Amount) * Convert.ToDecimal(complexityPoints.Value);

                // Aggregate by month (first day of month as key)
                var monthKey = new DateTime(record.Date.Year, record.Date.Month, 1);
                if (!producedCPByMonth.ContainsKey(monthKey))
                    producedCPByMonth[monthKey] = 0;

                producedCPByMonth[monthKey] += producedCP;
            }
        }

        return producedCPByMonth;
    }

    private Dictionary<DateTime, decimal> CalculateM1_A_PerMonth(
        double productComplexityPoints,
        Dictionary<DateTime, decimal> companyWideProducedCP,
        List<MonthlyCost> m1Costs,
        DateOnly dateFrom,
        DateOnly dateTo)
    {
        var m1_A_ByMonth = new Dictionary<DateTime, decimal>();

        // Generate list of months in the date range
        var currentDate = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var endDateTime = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (currentDate <= endDateTime)
        {
            // Define 12-month reference period ending at current month
            var referenceStart = currentDate.AddMonths(-11);
            var referenceEnd = currentDate;

            // Sum M1 costs in reference period
            var totalM1Costs = m1Costs
                .Where(c => c.Month >= referenceStart && c.Month <= referenceEnd)
                .Sum(c => c.Cost);

            // Sum produced CP in reference period
            var totalProducedCP = companyWideProducedCP
                .Where(kvp => kvp.Key >= referenceStart && kvp.Key <= referenceEnd)
                .Sum(kvp => kvp.Value);

            // Calculate M1_A
            decimal m1_A;
            if (totalProducedCP > 0)
            {
                var costPerCP = totalM1Costs / totalProducedCP;
                m1_A = (decimal)productComplexityPoints * costPerCP;
            }
            else
            {
                _logger.LogWarning(
                    "No production data in reference period ending {Month}, M1_A set to 0",
                    currentDate);
                m1_A = 0;
            }

            m1_A_ByMonth[currentDate] = m1_A;
            currentDate = currentDate.AddMonths(1);
        }

        return m1_A_ByMonth;
    }

    private Dictionary<DateTime, decimal?> CalculateM1_B_PerMonth(
        CatalogAggregate product,
        double productComplexityPoints,
        Dictionary<DateTime, decimal> companyWideProducedCP,
        List<MonthlyCost> m1Costs)
    {
        var m1_B_ByMonth = new Dictionary<DateTime, decimal?>();

        // Check which months this product was produced
        var productionMonths = product.ManufactureHistory
            ?.GroupBy(h => new DateTime(h.Date.Year, h.Date.Month, 1))
            .Select(g => g.Key)
            .ToHashSet() ?? new HashSet<DateTime>();

        // For each month in company-wide data
        foreach (var monthKey in companyWideProducedCP.Keys)
        {
            // Check if this product was produced in this month
            if (!productionMonths.Contains(monthKey))
            {
                m1_B_ByMonth[monthKey] = null; // Not produced
                continue;
            }

            // Get M1 costs for this month
            var m1CostForMonth = m1Costs.FirstOrDefault(c => c.Month == monthKey)?.Cost ?? 0;

            // Get total produced CP for this month
            var totalProducedCP = companyWideProducedCP[monthKey];

            // Calculate M1_B
            if (totalProducedCP > 0)
            {
                var m1_B_per_CP = m1CostForMonth / totalProducedCP;
                var m1_B = (decimal)productComplexityPoints * m1_B_per_CP;
                m1_B_ByMonth[monthKey] = m1_B;
            }
            else
            {
                m1_B_ByMonth[monthKey] = null; // No production company-wide
            }
        }

        return m1_B_ByMonth;
    }

    private MarginData CalculateMarginAverages(List<MonthlyMarginData> monthlyData)
    {
        var validData = monthlyData.Where(m => m.M0.Percentage > 0).ToList();

        if (!validData.Any())
        {
            return new MarginData();
        }

        // For M1_B, only average months where it's not null (product was produced)
        var validM1_B_Data = validData.Where(m => m.M1_B != null).ToList();

        return new MarginData
        {
            M0 = new MarginLevel(
                validData.Average(m => m.M0.Percentage),
                validData.Average(m => m.M0.Amount),
                validData.Average(m => m.M0.CostTotal),
                validData.Average(m => m.M0.CostLevel)
            ),
            M1_A = new MarginLevel(
                validData.Average(m => m.M1_A.Percentage),
                validData.Average(m => m.M1_A.Amount),
                validData.Average(m => m.M1_A.CostTotal),
                validData.Average(m => m.M1_A.CostLevel)
            ),
            M1_B = validM1_B_Data.Any()
                ? new MarginLevel(
                    validM1_B_Data.Average(m => m.M1_B!.Percentage),
                    validM1_B_Data.Average(m => m.M1_B!.Amount),
                    validM1_B_Data.Average(m => m.M1_B!.CostTotal),
                    validM1_B_Data.Average(m => m.M1_B!.CostLevel)
                )
                : new MarginLevel(0, 0, 0, 0),
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