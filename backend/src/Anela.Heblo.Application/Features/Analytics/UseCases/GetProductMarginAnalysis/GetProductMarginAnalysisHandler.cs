using Anela.Heblo.Application.Features.Analytics.Contracts;
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
    private readonly IMarginCalculator _marginCalculator;

    public GetProductMarginAnalysisHandler(
        IAnalyticsRepository analyticsRepository,
        IReportBuilderService reportBuilderService,
        IMarginCalculator marginCalculator)
    {
        _analyticsRepository = analyticsRepository;
        _reportBuilderService = reportBuilderService;
        _marginCalculator = marginCalculator;
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
            var marginData = _marginCalculator.CalculateForProduct(productData, productData.SalesHistory);

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
            response.MonthlyBreakdown = (_reportBuilderService
                .BuildMonthlyBreakdown(productData.SalesHistory, productData, request.StartDate, request.EndDate) ?? [])
                .Select(dto => new GetProductMarginAnalysisResponse.MonthlyMarginBreakdown
                {
                    Month = dto.Month,
                    MarginAmount = dto.MarginAmount,
                    Revenue = dto.Revenue,
                    Cost = dto.Cost,
                    UnitsSold = dto.UnitsSold
                })
                .ToList();
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

}