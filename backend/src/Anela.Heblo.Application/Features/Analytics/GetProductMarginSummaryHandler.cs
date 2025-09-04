using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics;

/// <summary>
/// 🔒 PERFORMANCE FIX: Refactored handler using streaming architecture
/// Extracted complex logic to dedicated calculators, reduced memory usage
/// </summary>
public class GetProductMarginSummaryHandler : IRequestHandler<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IProductMarginAnalysisService _marginAnalysisService;
    private readonly MarginCalculator _marginCalculator;
    private readonly MonthlyBreakdownGenerator _monthlyBreakdownGenerator;

    public GetProductMarginSummaryHandler(
        IAnalyticsRepository analyticsRepository,
        IProductMarginAnalysisService marginAnalysisService,
        MarginCalculator marginCalculator,
        MonthlyBreakdownGenerator monthlyBreakdownGenerator)
    {
        _analyticsRepository = analyticsRepository;
        _marginAnalysisService = marginAnalysisService;
        _marginCalculator = marginCalculator;
        _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
    }

    public async Task<GetProductMarginSummaryResponse> Handle(GetProductMarginSummaryRequest request, CancellationToken cancellationToken)
    {
        // 1. Parse time window and calculate date range
        var (fromDate, toDate) = _marginAnalysisService.ParseTimeWindow(request.TimeWindow);
        var dateRange = new DateRange(fromDate, toDate);

        // 2. Stream products with Product/Goods types that have sales in the period  
        var productTypes = new[] { ProductType.Product, ProductType.Goods };
        var productStream = _analyticsRepository.StreamProductsWithSalesAsync(fromDate, toDate, productTypes, cancellationToken);

        // 3. Calculate margin data using streaming approach (reduces memory usage)
        var calculationResult = await _marginCalculator.CalculateAsync(productStream, dateRange, request.GroupingMode, cancellationToken);

        // 4. Generate top products list from calculated results
        var allGroups = GenerateTopProducts(calculationResult, request.GroupingMode);

        // 5. Generate monthly breakdown using extracted generator (only if we have results)
        var monthlyData = calculationResult.TotalMargin == 0 && !calculationResult.GroupTotals.Any()
            ? new List<MonthlyProductMarginDto>()
            : _monthlyBreakdownGenerator.Generate(calculationResult, dateRange, request.GroupingMode);

        return new GetProductMarginSummaryResponse
        {
            MonthlyData = monthlyData,
            TopProducts = allGroups,
            TotalMargin = calculationResult.TotalMargin,
            TimeWindow = request.TimeWindow,
            GroupingMode = request.GroupingMode,
            FromDate = fromDate,
            ToDate = toDate
        };
    }

    /// <summary>
    /// 🔒 PERFORMANCE FIX: Simplified top products generation using calculation results
    /// No longer requires full product list in memory
    /// </summary>
    private List<TopProductDto> GenerateTopProducts(MarginCalculationResult calculationResult, ProductGroupingMode groupingMode)
    {
        return calculationResult.GroupTotals
            .OrderByDescending(kvp => kvp.Value)
            .Select((kvp, index) =>
            {
                var displayName = _marginCalculator.GetGroupDisplayName(kvp.Key, groupingMode, calculationResult.GroupProducts[kvp.Key]);
                return new TopProductDto
                {
                    GroupKey = kvp.Key,
                    DisplayName = displayName,
                    TotalMargin = kvp.Value,
                    ColorCode = "", // Color will be assigned on frontend
                    Rank = index + 1
                };
            })
            .ToList();
    }

}