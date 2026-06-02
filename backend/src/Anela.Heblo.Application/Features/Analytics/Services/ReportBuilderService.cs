using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Models;
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface IReportBuilderService
{
    List<MonthlyMarginBreakdownDto> BuildMonthlyBreakdown(
        List<SalesDataPoint> salesData,
        AnalyticsProduct productData,
        DateTime startDate,
        DateTime endDate);

    List<CategoryMarginSummaryDto> BuildCategorySummaries(
        Dictionary<string, CategoryData> categoryTotals);

    ProductMarginSummaryDto BuildProductSummary(
        AnalyticsProduct product,
        AnalysisMarginData marginData);
}

public class ReportBuilderService : IReportBuilderService
{
    private readonly IMarginCalculator _marginCalculator;

    public ReportBuilderService(IMarginCalculator marginCalculator)
    {
        _marginCalculator = marginCalculator;
    }

    public List<MonthlyMarginBreakdownDto> BuildMonthlyBreakdown(
        List<SalesDataPoint> salesData,
        AnalyticsProduct productData,
        DateTime startDate,
        DateTime endDate)
    {
        var breakdown = new List<MonthlyMarginBreakdownDto>();
        var current = new DateTime(startDate.Year, startDate.Month, 1);
        var end = new DateTime(endDate.Year, endDate.Month, 1);

        while (current <= end)
        {
            var monthSales = salesData
                .Where(s => s.Date.Year == current.Year && s.Date.Month == current.Month)
                .ToList();

            var monthData = _marginCalculator.CalculateForProduct(productData, monthSales);

            breakdown.Add(new MonthlyMarginBreakdownDto
            {
                Month = current,
                MarginAmount = monthData.Margin,
                Revenue = monthData.Revenue,
                Cost = monthData.Cost,
                UnitsSold = monthData.UnitsSold
            });

            current = current.AddMonths(1);
        }

        return breakdown;
    }

    public List<CategoryMarginSummaryDto> BuildCategorySummaries(
        Dictionary<string, CategoryData> categoryTotals)
    {
        return categoryTotals.Select(kvp => new CategoryMarginSummaryDto
        {
            Category = kvp.Key,
            TotalMargin = kvp.Value.TotalMargin,
            TotalRevenue = kvp.Value.TotalRevenue,
            AverageMarginPercentage = kvp.Value.TotalRevenue > 0 ?
                (kvp.Value.TotalMargin / kvp.Value.TotalRevenue) * 100 : 0,
            ProductCount = kvp.Value.ProductCount,
            TotalUnitsSold = kvp.Value.TotalUnitsSold
        }).ToList();
    }

    public ProductMarginSummaryDto BuildProductSummary(
        AnalyticsProduct product,
        AnalysisMarginData marginData)
    {
        return new ProductMarginSummaryDto
        {
            ProductId = product.ProductCode,
            ProductName = product.ProductName,
            Category = product.ProductCategory ?? AnalyticsConstants.DEFAULT_CATEGORY,
            MarginAmount = marginData.Margin,

            // M0-M2 margin levels - amounts
            M0Amount = product.M0Amount,
            M1Amount = product.M1Amount,
            M2Amount = product.M2Amount,

            // M0-M2 margin levels - percentages
            M0Percentage = product.M0Percentage,
            M1Percentage = product.M1Percentage,
            M2Percentage = product.M2Percentage,

            // Pricing
            SellingPrice = product.SellingPrice,
            PurchasePrice = product.PurchasePrice,

            MarginPercentage = marginData.MarginPercentage,
            Revenue = marginData.Revenue,
            Cost = marginData.Cost,
            UnitsSold = marginData.UnitsSold
        };
    }
}
