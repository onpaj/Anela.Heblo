using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Domain.Features.Analytics;
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginSummary;

/// <summary>
/// 🔒 PERFORMANCE FIX: Refactored handler using streaming architecture
/// Extracted complex logic to dedicated calculators, reduced memory usage
/// </summary>
public class GetProductMarginSummaryHandler : IRequestHandler<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IMarginCalculator _marginCalculator;
    private readonly IMonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly ITopProductSorter _topProductSorter;
    private readonly TimeWindowParser _timeWindowParser;

    public GetProductMarginSummaryHandler(
        IAnalyticsRepository analyticsRepository,
        IMarginCalculator marginCalculator,
        IMonthlyBreakdownGenerator monthlyBreakdownGenerator,
        ITopProductSorter topProductSorter,
        TimeWindowParser timeWindowParser)
    {
        _analyticsRepository = analyticsRepository;
        _marginCalculator = marginCalculator;
        _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
        _topProductSorter = topProductSorter;
        _timeWindowParser = timeWindowParser;
    }

    public async Task<GetProductMarginSummaryResponse> Handle(GetProductMarginSummaryRequest request, CancellationToken cancellationToken)
    {
        // 1. Parse time window and calculate date range
        var (fromDate, toDate) = _timeWindowParser.ParseTimeWindow(request.TimeWindow);
        var dateRange = new DateRange(fromDate, toDate);

        // 2. Stream products with Product/Goods types that have sales in the period
        var productTypes = new[] { AnalyticsProductType.Product, AnalyticsProductType.Goods };
        var productStream = _analyticsRepository.StreamProductsWithSalesAsync(fromDate, toDate, productTypes, cancellationToken);

        // 3. Calculate margin data using streaming approach (reduces memory usage)
        var calculationResult = await _marginCalculator.CalculateAsync(productStream, dateRange, request.GroupingMode, request.MarginLevel, cancellationToken);

        // 4. Generate top products list from calculated results
        var allGroups = GenerateTopProducts(calculationResult, request.GroupingMode, request.SortBy, request.SortDescending, request.MarginLevel);

        // 5. Generate monthly breakdown using extracted generator (only if we have results)
        var monthlyData = calculationResult.TotalMargin == 0 && !calculationResult.GroupTotals.Any()
            ? new List<MonthlyProductMarginDto>()
            : _monthlyBreakdownGenerator.Generate(calculationResult, dateRange, request.GroupingMode, request.MarginLevel);

        return new GetProductMarginSummaryResponse
        {
            MonthlyData = monthlyData,
            TopProducts = allGroups,
            TotalMargin = calculationResult.TotalMargin,
            TimeWindow = request.TimeWindow,
            GroupingMode = request.GroupingMode,
            MarginLevel = request.MarginLevel,
            FromDate = fromDate,
            ToDate = toDate
        };
    }

    /// <summary>
    /// 🔒 PERFORMANCE FIX: Simplified top products generation using calculation results
    /// No longer requires full product list in memory
    /// </summary>
    private List<TopProductDto> GenerateTopProducts(MarginCalculationResult calculationResult, ProductGroupingMode groupingMode, string? sortBy, bool sortDescending, MarginLevel marginLevel)
    {
        var topProductsWithData = calculationResult.GroupTotals
            .Select(kvp =>
            {
                var displayName = _marginCalculator.GetGroupDisplayName(kvp.Key, groupingMode, calculationResult.GroupProducts[kvp.Key]);
                var products = calculationResult.GroupProducts[kvp.Key];

                // Calculate aggregated margin data for the group
                var groupData = _marginCalculator.GetGroupAggregatedMarginData(products);

                // Calculate total margin based on selected margin level
                var totalMarginForLevel = CalculateTotalMarginForLevel(products, marginLevel);

                return new TopProductDto
                {
                    GroupKey = kvp.Key,
                    DisplayName = displayName,
                    TotalMargin = totalMarginForLevel,
                    ColorCode = "", // Color will be assigned on frontend

                    // M0-M2 margin levels - amounts (averaged)
                    M0Amount = groupData.M0Amount,
                    M1Amount = groupData.M1Amount,
                    M2Amount = groupData.M2Amount,

                    // M0-M2 margin levels - percentages (averaged)
                    M0Percentage = groupData.M0Percentage,
                    M1Percentage = groupData.M1Percentage,
                    M2Percentage = groupData.M2Percentage,

                    // Pricing (averaged)
                    SellingPrice = groupData.SellingPrice,
                    PurchasePrice = groupData.PurchasePrice
                };
            })
            .ToList();

        // Apply sorting
        var sortedProducts = _topProductSorter.Sort(topProductsWithData, sortBy, sortDescending);

        // Add rank after sorting
        for (int i = 0; i < sortedProducts.Count; i++)
        {
            sortedProducts[i].Rank = i + 1;
        }

        return sortedProducts;
    }

    /// <summary>
    /// Calculates total margin for a group of products based on selected margin level
    /// </summary>
    private decimal CalculateTotalMarginForLevel(List<AnalyticsProduct> products, MarginLevel marginLevel)
    {
        return products.Sum(p =>
            (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)
            * _marginCalculator.GetMarginAmountForLevel(p, marginLevel));
    }

}