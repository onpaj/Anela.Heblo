using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Models;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginAnalysis;
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface IReportBuilderService
{
    List<GetProductMarginAnalysisResponse.MonthlyMarginBreakdown> BuildMonthlyBreakdown(
        List<SalesDataPoint> salesData,
        AnalyticsProduct productData,
        DateTime startDate,
        DateTime endDate);

    List<GetMarginReportResponse.CategoryMarginSummary> BuildCategorySummaries(
        Dictionary<string, CategoryData> categoryTotals);

    GetMarginReportResponse.ProductMarginSummary BuildProductSummary(
        AnalyticsProduct product,
        AnalysisMarginData marginData);
}

public class ReportBuilderService : IReportBuilderService
{
    public List<GetProductMarginAnalysisResponse.MonthlyMarginBreakdown> BuildMonthlyBreakdown(
        List<SalesDataPoint> salesData,
        AnalyticsProduct productData,
        DateTime startDate,
        DateTime endDate)
    {
        var breakdown = new List<GetProductMarginAnalysisResponse.MonthlyMarginBreakdown>();
        var current = new DateTime(startDate.Year, startDate.Month, 1);
        var end = new DateTime(endDate.Year, endDate.Month, 1);

        while (current <= end)
        {
            var monthSales = salesData
                .Where(s => s.Date.Year == current.Year && s.Date.Month == current.Month)
                .ToList();

            var monthlyUnitsSold = (int)monthSales.Sum(s => s.AmountB2B + s.AmountB2C);
            var monthlyRevenue = (decimal)monthlyUnitsSold * productData.SellingPrice;
            var monthlyCost = (decimal)monthlyUnitsSold * (productData.SellingPrice - productData.MarginAmount);
            var monthlyMargin = monthlyRevenue - monthlyCost;

            breakdown.Add(new GetProductMarginAnalysisResponse.MonthlyMarginBreakdown
            {
                Month = current,
                MarginAmount = monthlyMargin,
                Revenue = monthlyRevenue,
                Cost = monthlyCost,
                UnitsSold = monthlyUnitsSold
            });

            current = current.AddMonths(1);
        }

        return breakdown;
    }

    public List<GetMarginReportResponse.CategoryMarginSummary> BuildCategorySummaries(
        Dictionary<string, CategoryData> categoryTotals)
    {
        return categoryTotals.Select(kvp => new GetMarginReportResponse.CategoryMarginSummary
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

    public GetMarginReportResponse.ProductMarginSummary BuildProductSummary(
        AnalyticsProduct product,
        AnalysisMarginData marginData)
    {
        return new GetMarginReportResponse.ProductMarginSummary
        {
            ProductId = product.ProductCode,
            ProductName = product.ProductName,
            Category = product.ProductCategory ?? AnalyticsConstants.DEFAULT_CATEGORY,
            MarginAmount = marginData.Margin,
            
            // M0-M3 margin levels - amounts
            M0Amount = product.M0Amount,
            M1Amount = product.M1Amount,
            M2Amount = product.M2Amount,
            M3Amount = product.M3Amount,
            
            // M0-M3 margin levels - percentages
            M0Percentage = product.M0Percentage,
            M1Percentage = product.M1Percentage,
            M2Percentage = product.M2Percentage,
            M3Percentage = product.M3Percentage,
            
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