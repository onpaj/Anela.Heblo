using System.Globalization;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Services;

/// <summary>
/// 🔒 PERFORMANCE FIX: Extracted monthly breakdown logic from handler
/// Implements single responsibility principle and reduces handler complexity
/// </summary>
public class MonthlyBreakdownGenerator
{
    private readonly MarginCalculator _marginCalculator;

    public MonthlyBreakdownGenerator(MarginCalculator marginCalculator)
    {
        _marginCalculator = marginCalculator;
    }

    /// <summary>
    /// Generates monthly breakdown efficiently by processing products once per month
    /// </summary>
    public List<MonthlyProductMarginDto> Generate(
        MarginCalculationResult calculationResult,
        DateRange dateRange,
        ProductGroupingMode groupingMode)
    {
        var monthlyData = new List<MonthlyProductMarginDto>();

        // Generate all months in the date range
        var current = new DateTime(dateRange.FromDate.Year, dateRange.FromDate.Month, 1);
        var end = new DateTime(dateRange.ToDate.Year, dateRange.ToDate.Month, 1);

        while (current <= end)
        {
            var monthStart = current;
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var monthlySegments = GenerateMonthlySegments(
                calculationResult.GroupProducts,
                monthStart,
                monthEnd,
                groupingMode);

            monthlyData.Add(new MonthlyProductMarginDto
            {
                Year = current.Year,
                Month = current.Month,
                MonthDisplay = current.ToString("MMM yyyy", CultureInfo.CreateSpecificCulture("cs-CZ")),
                ProductSegments = monthlySegments.segments,
                TotalMonthMargin = monthlySegments.totalMargin
            });

            current = current.AddMonths(1);
        }

        return monthlyData;
    }

    /// <summary>
    /// Generates segments for a specific month, processing each group's products efficiently
    /// </summary>
    private (List<ProductMarginSegmentDto> segments, decimal totalMargin) GenerateMonthlySegments(
        Dictionary<string, List<AnalyticsProduct>> groupProducts,
        DateTime monthStart,
        DateTime monthEnd,
        ProductGroupingMode groupingMode)
    {
        var segments = new List<ProductMarginSegmentDto>();
        var totalMonthMargin = 0m;

        foreach (var (groupKey, products) in groupProducts)
        {
            var groupData = ProcessGroupForMonth(products, monthStart, monthEnd);

            if (groupData.totalMargin <= 0)
                continue;

            totalMonthMargin += groupData.totalMargin;

            var displayName = _marginCalculator.GetGroupDisplayName(groupKey, groupingMode, products);

            segments.Add(new ProductMarginSegmentDto
            {
                GroupKey = groupKey,
                DisplayName = displayName,
                MarginContribution = groupData.totalMargin,
                ColorCode = "", // Color assigned by frontend
                AverageMarginPerPiece = groupData.avgMarginPerPiece,
                UnitsSold = groupData.totalUnitsSold,
                AverageSellingPriceWithoutVat = groupData.avgSellingPrice,
                AverageMaterialCosts = groupData.avgMaterialCosts,
                AverageLaborCosts = groupData.avgLaborCosts,
                ProductCount = groupData.productCount,
                IsOther = false
            });
        }

        // Sort by margin contribution (highest first)
        segments = segments.OrderByDescending(s => s.MarginContribution).ToList();

        // Calculate percentages
        if (totalMonthMargin > 0)
        {
            foreach (var segment in segments)
            {
                segment.Percentage = (segment.MarginContribution / totalMonthMargin) * 100;
            }
        }

        return (segments, totalMonthMargin);
    }

    /// <summary>
    /// Processes a group of products for a specific month
    /// </summary>
    private (decimal totalMargin, int totalUnitsSold, decimal avgMarginPerPiece,
             decimal avgSellingPrice, decimal avgMaterialCosts, decimal avgLaborCosts,
             int productCount) ProcessGroupForMonth(
        List<AnalyticsProduct> products,
        DateTime monthStart,
        DateTime monthEnd)
    {
        var totalMargin = 0m;
        var totalUnitsSold = 0;
        var productCount = 0;
        var totalMarginPerPiece = 0m;
        var totalSellingPrice = 0m;
        var totalMaterialCosts = 0m;
        var totalLaborCosts = 0m;

        foreach (var product in products)
        {
            var salesInMonth = product.SalesHistory
                .Where(s => s.Date >= monthStart && s.Date <= monthEnd)
                .ToList();

            if (!salesInMonth.Any() || product.MarginAmount <= 0)
                continue;

            var unitsSold = (int)salesInMonth.Sum(s => s.AmountB2B + s.AmountB2C);
            var marginContribution = unitsSold * product.MarginAmount;

            totalMargin += marginContribution;
            totalUnitsSold += unitsSold;
            productCount++;

            // Accumulate for averages
            totalMarginPerPiece += product.MarginAmount;
            totalSellingPrice += product.EshopPriceWithoutVat ?? 0;
            totalMaterialCosts += product.MaterialCost;
            totalLaborCosts += product.HandlingCost;
        }

        return (
            totalMargin,
            totalUnitsSold,
            productCount > 0 ? totalMarginPerPiece / productCount : 0,
            productCount > 0 ? totalSellingPrice / productCount : 0,
            productCount > 0 ? totalMaterialCosts / productCount : 0,
            productCount > 0 ? totalLaborCosts / productCount : 0,
            productCount
        );
    }
}