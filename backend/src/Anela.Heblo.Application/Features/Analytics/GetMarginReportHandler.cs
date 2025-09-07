using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics;

/// <summary>
/// Handler for generating comprehensive margin reports across multiple products
/// Uses result-based error handling instead of throwing exceptions
/// </summary>
public class GetMarginReportHandler : IRequestHandler<GetMarginReportRequest, GetMarginReportResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;

    public GetMarginReportHandler(IAnalyticsRepository analyticsRepository)
    {
        _analyticsRepository = analyticsRepository;
    }

    public async Task<GetMarginReportResponse> Handle(GetMarginReportRequest request, CancellationToken cancellationToken)
    {
        // Validate date range
        if (request.StartDate > request.EndDate)
        {
            return new GetMarginReportResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidDateRange,
                Params = new Dictionary<string, string>
                {
                    { "startDate", request.StartDate.ToString("yyyy-MM-dd") },
                    { "endDate", request.EndDate.ToString("yyyy-MM-dd") }
                }
            };
        }

        // Validate report period (must be reasonable)
        var periodDays = (request.EndDate - request.StartDate).TotalDays;
        if (periodDays > 365 * 2) // More than 2 years
        {
            return new GetMarginReportResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidReportPeriod,
                Params = new Dictionary<string, string>
                {
                    { "period", $"{periodDays:F0} days" }
                }
            };
        }

        if (periodDays <= 0)
        {
            return new GetMarginReportResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidReportPeriod,
                Params = new Dictionary<string, string>
                {
                    { "period", "Zero or negative days" }
                }
            };
        }

        try
        {
            // Get products with sales in the period
            var productTypes = new[] { ProductType.Product, ProductType.Goods };
            var productsStream = _analyticsRepository.StreamProductsWithSalesAsync(
                request.StartDate, 
                request.EndDate, 
                productTypes, 
                cancellationToken);

            var products = new List<Domain.Features.Analytics.AnalyticsProduct>();
            await foreach (var product in productsStream.WithCancellation(cancellationToken))
            {
                // Apply filters if specified
                if (!string.IsNullOrWhiteSpace(request.ProductFilter) && 
                    !product.ProductName.Contains(request.ProductFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(request.CategoryFilter) && 
                    !string.Equals(product.ProductCategory, request.CategoryFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                products.Add(product);
            }

            if (!products.Any())
            {
                return new GetMarginReportResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.AnalysisDataNotAvailable,
                    Params = new Dictionary<string, string>
                    {
                        { "product", request.ProductFilter ?? "all products" },
                        { "period", $"{request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}" }
                    }
                };
            }

            // Limit products to avoid performance issues
            if (products.Count > request.MaxProducts)
            {
                products = products.Take(request.MaxProducts).ToList();
            }

            // Calculate report data
            var productSummaries = new List<GetMarginReportResponse.ProductMarginSummary>();
            var categoryTotals = new Dictionary<string, CategoryData>();

            decimal totalMargin = 0;
            decimal totalRevenue = 0;
            decimal totalCost = 0;
            int totalUnitsSold = 0;

            foreach (var product in products)
            {
                var salesInPeriod = product.SalesHistory
                    .Where(s => s.Date >= request.StartDate && s.Date <= request.EndDate)
                    .ToList();

                var unitsSold = (int)salesInPeriod.Sum(s => s.AmountB2B + s.AmountB2C);
                if (unitsSold <= 0) continue;

                try
                {
                    var revenue = (decimal)unitsSold * product.SellingPrice;
                    var cost = (decimal)unitsSold * (product.SellingPrice - product.MarginAmount);
                    var margin = revenue - cost;
                    var marginPercentage = revenue > 0 ? (margin / revenue) * 100 : 0;

                    productSummaries.Add(new GetMarginReportResponse.ProductMarginSummary
                    {
                        ProductId = product.ProductCode,
                        ProductName = product.ProductName,
                        Category = product.ProductCategory ?? "Uncategorized",
                        MarginAmount = margin,
                        MarginPercentage = marginPercentage,
                        Revenue = revenue,
                        Cost = cost,
                        UnitsSold = unitsSold
                    });

                    // Accumulate category data
                    var category = product.ProductCategory ?? "Uncategorized";
                    if (!categoryTotals.ContainsKey(category))
                    {
                        categoryTotals[category] = new CategoryData();
                    }
                    
                    categoryTotals[category].TotalMargin += margin;
                    categoryTotals[category].TotalRevenue += revenue;
                    categoryTotals[category].ProductCount++;
                    categoryTotals[category].TotalUnitsSold += unitsSold;

                    // Accumulate overall totals
                    totalMargin += margin;
                    totalRevenue += revenue;
                    totalCost += cost;
                    totalUnitsSold += unitsSold;
                }
                catch (Exception ex)
                {
                    return new GetMarginReportResponse
                    {
                        Success = false,
                        ErrorCode = ErrorCodes.MarginCalculationFailed,
                        Params = new Dictionary<string, string>
                        {
                            { "reason", $"Error calculating margins for product {product.ProductName}: {ex.Message}" }
                        }
                    };
                }
            }

            if (productSummaries.Count == 0)
            {
                return new GetMarginReportResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.InsufficientData,
                    Params = new Dictionary<string, string>
                    {
                        { "requiredPeriod", $"{request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}" }
                    }
                };
            }

            // Generate category summaries
            var categorySummaries = categoryTotals.Select(kvp => new GetMarginReportResponse.CategoryMarginSummary
            {
                Category = kvp.Key,
                TotalMargin = kvp.Value.TotalMargin,
                TotalRevenue = kvp.Value.TotalRevenue,
                AverageMarginPercentage = kvp.Value.TotalRevenue > 0 ? 
                    (kvp.Value.TotalMargin / kvp.Value.TotalRevenue) * 100 : 0,
                ProductCount = kvp.Value.ProductCount,
                TotalUnitsSold = kvp.Value.TotalUnitsSold
            }).ToList();

            // Sort products by margin (descending)
            productSummaries = productSummaries.OrderByDescending(p => p.MarginAmount).ToList();

            var averageMarginPercentage = totalRevenue > 0 ? (totalMargin / totalRevenue) * 100 : 0;

            return new GetMarginReportResponse
            {
                Success = true,
                ReportPeriodStart = request.StartDate,
                ReportPeriodEnd = request.EndDate,
                TotalMargin = totalMargin,
                TotalRevenue = totalRevenue,
                TotalCost = totalCost,
                AverageMarginPercentage = averageMarginPercentage,
                TotalProductsAnalyzed = productSummaries.Count,
                TotalUnitsSold = totalUnitsSold,
                ProductSummaries = productSummaries,
                CategorySummaries = categorySummaries
            };
        }
        catch (Exception ex)
        {
            return new GetMarginReportResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InternalServerError,
                Params = new Dictionary<string, string>
                {
                    { "details", ex.Message }
                }
            };
        }
    }

    private class CategoryData
    {
        public decimal TotalMargin { get; set; }
        public decimal TotalRevenue { get; set; }
        public int ProductCount { get; set; }
        public int TotalUnitsSold { get; set; }
    }
}