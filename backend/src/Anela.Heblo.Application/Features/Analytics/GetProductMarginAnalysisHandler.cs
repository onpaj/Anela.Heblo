using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Analytics;
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics;

/// <summary>
/// Handler for retrieving detailed margin analysis for a specific product
/// Uses result-based error handling instead of throwing exceptions
/// </summary>
public class GetProductMarginAnalysisHandler : IRequestHandler<GetProductMarginAnalysisRequest, GetProductMarginAnalysisResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;

    public GetProductMarginAnalysisHandler(IAnalyticsRepository analyticsRepository)
    {
        _analyticsRepository = analyticsRepository;
    }

    public async Task<GetProductMarginAnalysisResponse> Handle(GetProductMarginAnalysisRequest request, CancellationToken cancellationToken)
    {
        // Validate date range
        if (request.StartDate > request.EndDate)
        {
            return new GetProductMarginAnalysisResponse
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

        if (string.IsNullOrWhiteSpace(request.ProductId))
        {
            return new GetProductMarginAnalysisResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.RequiredFieldMissing,
                Params = new Dictionary<string, string>
                {
                    { "field", "ProductId" }
                }
            };
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
                return new GetProductMarginAnalysisResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ProductNotFoundForAnalysis,
                    Params = new Dictionary<string, string>
                    {
                        { "productId", request.ProductId }
                    }
                };
            }

            // Check if we have sales data for the period
            if (!productData.SalesHistory.Any(s => s.Date >= request.StartDate && s.Date <= request.EndDate))
            {
                return new GetProductMarginAnalysisResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.AnalysisDataNotAvailable,
                    Params = new Dictionary<string, string>
                    {
                        { "product", productData.ProductName },
                        { "period", $"{request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}" }
                    }
                };
            }

            // Calculate margin analysis
            var salesInPeriod = productData.SalesHistory
                .Where(s => s.Date >= request.StartDate && s.Date <= request.EndDate)
                .ToList();

            var totalUnitsSold = (int)salesInPeriod.Sum(s => s.AmountB2B + s.AmountB2C);
            
            if (totalUnitsSold <= 0)
            {
                return new GetProductMarginAnalysisResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.InsufficientData,
                    Params = new Dictionary<string, string>
                    {
                        { "requiredPeriod", $"{request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}" }
                    }
                };
            }

            // Calculate financial metrics
            decimal totalRevenue = 0;
            decimal totalCost = 0;
            
            try
            {
                totalRevenue = (decimal)totalUnitsSold * productData.SellingPrice;
                totalCost = (decimal)totalUnitsSold * (productData.SellingPrice - productData.MarginAmount);
            }
            catch (Exception ex)
            {
                return new GetProductMarginAnalysisResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.MarginCalculationFailed,
                    Params = new Dictionary<string, string>
                    {
                        { "reason", ex.Message }
                    }
                };
            }

            var totalMargin = totalRevenue - totalCost;
            var marginPercentage = totalRevenue > 0 ? (totalMargin / totalRevenue) * 100 : 0;

            var response = new GetProductMarginAnalysisResponse
            {
                Success = true,
                ProductId = request.ProductId,
                ProductName = productData.ProductName,
                TotalMargin = totalMargin,
                MarginPercentage = marginPercentage,
                TotalRevenue = totalRevenue,
                TotalCost = totalCost,
                TotalUnitsSold = totalUnitsSold,
                AnalysisPeriodStart = request.StartDate,
                AnalysisPeriodEnd = request.EndDate
            };

            // Add monthly breakdown if requested
            if (request.IncludeBreakdown)
            {
                response.MonthlyBreakdown = GenerateMonthlyBreakdown(salesInPeriod, productData, request.StartDate, request.EndDate);
            }

            return response;
        }
        catch (Exception ex)
        {
            return new GetProductMarginAnalysisResponse
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

    private List<GetProductMarginAnalysisResponse.MonthlyMarginBreakdown> GenerateMonthlyBreakdown(
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
}