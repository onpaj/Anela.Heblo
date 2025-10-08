using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Analytics;
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginAnalysis;

/// <summary>
/// Handler for retrieving detailed margin analysis for a specific product
/// Uses result-based error handling instead of throwing exceptions
/// </summary>
public class GetProductMarginAnalysisHandler : IRequestHandler<GetProductMarginAnalysisRequest, GetProductMarginAnalysisResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IReportBuilderService _reportBuilderService;

    public GetProductMarginAnalysisHandler(
        IAnalyticsRepository analyticsRepository,
        IReportBuilderService reportBuilderService)
    {
        _analyticsRepository = analyticsRepository;
        _reportBuilderService = reportBuilderService;
    }

    public async Task<GetProductMarginAnalysisResponse> Handle(GetProductMarginAnalysisRequest request, CancellationToken cancellationToken)
    {
        // Basic input validation (kept here for backward compatibility with tests)
        if (string.IsNullOrWhiteSpace(request.ProductId))
        {
            return CreateErrorResponse(ErrorCodes.RequiredFieldMissing, ("field", "ProductId"));
        }

        if (request.StartDate > request.EndDate)
        {
            return CreateErrorResponse(ErrorCodes.InvalidDateRange,
                ("startDate", request.StartDate.ToString("yyyy-MM-dd")),
                ("endDate", request.EndDate.ToString("yyyy-MM-dd")));
        }

        try
        {
            // Get product data
            var productData = await _analyticsRepository.GetProductAnalysisDataAsync(
                request.ProductId,
                request.StartDate,
                request.EndDate,
                cancellationToken);

            if (productData == null)
            {
                return CreateErrorResponse(ErrorCodes.ProductNotFoundForAnalysis,
                    ("productId", request.ProductId));
            }

            // Check if we have sales data for the period
            if (!HasSalesInPeriod(productData, request.StartDate, request.EndDate))
            {
                return CreateErrorResponse(ErrorCodes.AnalysisDataNotAvailable,
                    ("product", productData.ProductName),
                    ("period", $"{request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}"));
            }

            // Calculate margins directly from product data
            var marginData = CalculateProductMargins(productData, request.StartDate, request.EndDate);

            // Build successful response
            return BuildSuccessResponse(request, productData, marginData);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(ErrorCodes.InternalServerError, ("details", ex.Message));
        }
    }

    private static bool HasSalesInPeriod(AnalyticsProduct productData, DateTime startDate, DateTime endDate)
    {
        return productData.SalesHistory.Any(s => s.Date >= startDate && s.Date <= endDate);
    }

    private GetProductMarginAnalysisResponse BuildSuccessResponse(
        GetProductMarginAnalysisRequest request,
        AnalyticsProduct productData,
        AnalysisMarginData marginData)
    {
        var response = new GetProductMarginAnalysisResponse
        {
            Success = true,
            ProductId = request.ProductId,
            ProductName = productData.ProductName,
            TotalMargin = marginData.Margin,
            MarginPercentage = marginData.MarginPercentage,
            TotalRevenue = marginData.Revenue,
            TotalCost = marginData.Cost,
            TotalUnitsSold = marginData.UnitsSold,
            AnalysisPeriodStart = request.StartDate,
            AnalysisPeriodEnd = request.EndDate
        };

        // Add monthly breakdown if requested
        if (request.IncludeBreakdown)
        {
            var salesInPeriod = productData.SalesHistory
                .Where(s => s.Date >= request.StartDate && s.Date <= request.EndDate)
                .ToList();

            response.MonthlyBreakdown = _reportBuilderService.BuildMonthlyBreakdown(
                salesInPeriod, productData, request.StartDate, request.EndDate);
        }

        return response;
    }

    private static GetProductMarginAnalysisResponse CreateErrorResponse(ErrorCodes errorCode, params (string key, string value)[] parameters)
    {
        return new GetProductMarginAnalysisResponse
        {
            Success = false,
            ErrorCode = errorCode,
            Params = parameters?.ToDictionary(p => p.key, p => p.value)
        };
    }

    /// <summary>
    /// Calculates margin data directly from product analytics data
    /// </summary>
    private static AnalysisMarginData CalculateProductMargins(AnalyticsProduct product, DateTime startDate, DateTime endDate)
    {
        var salesInPeriod = product.SalesHistory
            .Where(s => s.Date >= startDate && s.Date <= endDate)
            .ToList();

        var totalSales = salesInPeriod.Sum(s => s.AmountB2B + s.AmountB2C);
        var revenue = (decimal)totalSales * product.SellingPrice;
        var cost = (decimal)totalSales * (product.SellingPrice - product.MarginAmount);
        var margin = revenue - cost;
        var marginPercentage = revenue > 0 ? (margin / revenue) * 100 : 0;

        return new AnalysisMarginData
        {
            Revenue = revenue,
            Cost = cost,
            Margin = margin,
            MarginPercentage = marginPercentage,
            UnitsSold = (int)totalSales
        };
    }

}