using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.Models;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;

/// <summary>
/// Handler for generating comprehensive margin reports across multiple products
/// Uses result-based error handling instead of throwing exceptions
/// </summary>
public class GetMarginReportHandler : IRequestHandler<GetMarginReportRequest, GetMarginReportResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IProductFilterService _productFilterService;
    private readonly IReportBuilderService _reportBuilderService;

    public GetMarginReportHandler(
        IAnalyticsRepository analyticsRepository,
        IProductFilterService productFilterService,
        IReportBuilderService reportBuilderService)
    {
        _analyticsRepository = analyticsRepository;
        _productFilterService = productFilterService;
        _reportBuilderService = reportBuilderService;
    }

    public async Task<GetMarginReportResponse> Handle(GetMarginReportRequest request, CancellationToken cancellationToken)
    {
        // Basic input validation (kept here for backward compatibility with tests)
        if (request.StartDate > request.EndDate)
        {
            return CreateErrorResponse(ErrorCodes.InvalidDateRange,
                ("startDate", request.StartDate.ToString("yyyy-MM-dd")),
                ("endDate", request.EndDate.ToString("yyyy-MM-dd")));
        }

        var totalDays = (request.EndDate - request.StartDate).TotalDays;
        if (totalDays > AnalyticsConstants.MAX_REPORT_PERIOD_DAYS)
        {
            return CreateErrorResponse(ErrorCodes.InvalidReportPeriod,
                ("period", $"{totalDays} days (max {AnalyticsConstants.MAX_REPORT_PERIOD_DAYS})"));
        }

        if (totalDays < AnalyticsConstants.MIN_REPORT_PERIOD_DAYS)
        {
            return CreateErrorResponse(ErrorCodes.InvalidReportPeriod,
                ("period", $"{totalDays} days (min {AnalyticsConstants.MIN_REPORT_PERIOD_DAYS})"));
        }

        try
        {
            // Get products stream from repository
            var productTypes = new[] { ProductType.Product, ProductType.Goods };
            var productsStream = _analyticsRepository.StreamProductsWithSalesAsync(
                request.StartDate,
                request.EndDate,
                productTypes,
                cancellationToken);

            // Apply filters and get products list
            var products = await _productFilterService.FilterProductsAsync(
                productsStream,
                request.ProductFilter,
                request.CategoryFilter,
                request.MaxProducts,
                cancellationToken);

            if (!products.Any())
            {
                return CreateErrorResponse(ErrorCodes.AnalysisDataNotAvailable,
                    ("product", request.ProductFilter ?? "all products"),
                    ("period", $"{request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}"));
            }

            // Process products and calculate margins
            var reportData = await ProcessProductsForReport(products, request.StartDate, request.EndDate);

            if (reportData.ProductSummaries.Count == 0)
            {
                return CreateErrorResponse(ErrorCodes.InsufficientData,
                    ("requiredPeriod", $"{request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}"));
            }

            // Build final response
            return BuildSuccessResponse(request, reportData);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(ErrorCodes.InternalServerError, ("details", ex.Message));
        }
    }

    private async Task<ReportData> ProcessProductsForReport(
        List<Domain.Features.Analytics.AnalyticsProduct> products,
        DateTime startDate,
        DateTime endDate)
    {
        var productSummaries = new List<GetMarginReportResponse.ProductMarginSummary>();
        var categoryTotals = new Dictionary<string, CategoryData>();
        var overallTotals = new OverallTotals();

        foreach (var product in products)
        {
            // Check if product has sales data in the period
            if (!HasSalesInPeriod(product, startDate, endDate))
                continue;

            // Calculate basic margin data from sales and product data
            var totalSales = product.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C);
            var revenue = (decimal)totalSales * product.SellingPrice;
            var cost = (decimal)totalSales * (product.SellingPrice - product.MarginAmount);
            var margin = revenue - cost;
            var marginPercentage = revenue > 0 ? (margin / revenue) * 100 : 0;

            // Create margin data using the product's already calculated M0-M3 data
            var marginData = new AnalysisMarginData
            {
                Margin = margin,
                Revenue = revenue,
                Cost = cost,
                MarginPercentage = marginPercentage,
                UnitsSold = (int)totalSales
            };

            // Build product summary using the AnalyticsProduct data
            var productSummary = _reportBuilderService.BuildProductSummary(product, marginData);
            productSummaries.Add(productSummary);

            // Accumulate category totals
            AccumulateCategoryTotals(categoryTotals, product, marginData);

            // Accumulate overall totals
            overallTotals.Add(marginData);
        }

        // Sort products by M3 margin percentage (net profitability percentage, descending)
        productSummaries = productSummaries.OrderByDescending(p => p.M3Percentage).ToList();

        // Build category summaries
        var categorySummaries = _reportBuilderService.BuildCategorySummaries(categoryTotals);

        return new ReportData
        {
            ProductSummaries = productSummaries,
            CategorySummaries = categorySummaries,
            OverallTotals = overallTotals
        };
    }

    private static bool HasSalesInPeriod(Domain.Features.Analytics.AnalyticsProduct product, DateTime startDate, DateTime endDate)
    {
        return product.SalesHistory.Any(s => s.Date >= startDate && s.Date <= endDate);
    }

    private static void AccumulateCategoryTotals(
        Dictionary<string, CategoryData> categoryTotals,
        Domain.Features.Analytics.AnalyticsProduct product,
        AnalysisMarginData marginData)
    {
        var category = product.ProductCategory ?? AnalyticsConstants.DEFAULT_CATEGORY;
        if (!categoryTotals.ContainsKey(category))
        {
            categoryTotals[category] = new CategoryData();
        }

        categoryTotals[category].TotalMargin += marginData.Margin;
        categoryTotals[category].TotalRevenue += marginData.Revenue;
        categoryTotals[category].ProductCount++;
        categoryTotals[category].TotalUnitsSold += marginData.UnitsSold;
    }

    private GetMarginReportResponse BuildSuccessResponse(GetMarginReportRequest request, ReportData reportData)
    {
        var averageMarginPercentage = reportData.OverallTotals.TotalRevenue > 0
            ? (reportData.OverallTotals.TotalMargin / reportData.OverallTotals.TotalRevenue) * 100
            : 0;

        return new GetMarginReportResponse
        {
            Success = true,
            ReportPeriodStart = request.StartDate,
            ReportPeriodEnd = request.EndDate,
            TotalMargin = reportData.OverallTotals.TotalMargin,
            TotalRevenue = reportData.OverallTotals.TotalRevenue,
            TotalCost = reportData.OverallTotals.TotalCost,
            AverageMarginPercentage = averageMarginPercentage,
            TotalProductsAnalyzed = reportData.ProductSummaries.Count,
            TotalUnitsSold = reportData.OverallTotals.TotalUnitsSold,
            ProductSummaries = reportData.ProductSummaries,
            CategorySummaries = reportData.CategorySummaries
        };
    }

    private static GetMarginReportResponse CreateErrorResponse(ErrorCodes errorCode, params (string key, string value)[] parameters)
    {
        return new GetMarginReportResponse
        {
            Success = false,
            ErrorCode = errorCode,
            Params = parameters?.ToDictionary(p => p.key, p => p.value)
        };
    }

}

/// <summary>
/// Helper classes for organizing report calculation data
/// </summary>
internal class ReportData
{
    public List<GetMarginReportResponse.ProductMarginSummary> ProductSummaries { get; set; } = new();
    public List<GetMarginReportResponse.CategoryMarginSummary> CategorySummaries { get; set; } = new();
    public OverallTotals OverallTotals { get; set; } = new();
}

internal class OverallTotals
{
    public decimal TotalMargin { get; private set; }
    public decimal TotalRevenue { get; private set; }
    public decimal TotalCost { get; private set; }
    public int TotalUnitsSold { get; private set; }

    public void Add(AnalysisMarginData marginData)
    {
        TotalMargin += marginData.Margin;
        TotalRevenue += marginData.Revenue;
        TotalCost += marginData.Cost;
        TotalUnitsSold += marginData.UnitsSold;
    }
}