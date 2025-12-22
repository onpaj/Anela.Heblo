using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginSummary;

/// <summary>
/// 🔒 PERFORMANCE FIX: Refactored handler using streaming architecture
/// Extracted complex logic to dedicated calculators, reduced memory usage
/// </summary>
public class GetProductMarginSummaryHandler : IRequestHandler<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly MarginCalculator _marginCalculator;
    private readonly MonthlyBreakdownGenerator _monthlyBreakdownGenerator;

    public GetProductMarginSummaryHandler(
        IAnalyticsRepository analyticsRepository,
        MarginCalculator marginCalculator,
        MonthlyBreakdownGenerator monthlyBreakdownGenerator)
    {
        _analyticsRepository = analyticsRepository;
        _marginCalculator = marginCalculator;
        _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
    }

    public async Task<GetProductMarginSummaryResponse> Handle(GetProductMarginSummaryRequest request, CancellationToken cancellationToken)
    {
        // 1. Parse time window and calculate date range
        var (fromDate, toDate) = TimeWindowParser.ParseTimeWindow(request.TimeWindow);
        var dateRange = new DateRange(fromDate, toDate);

        // 2. Stream products with Product/Goods types that have sales in the period  
        var productTypes = new[] { ProductType.Product, ProductType.Goods };
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
    private List<TopProductDto> GenerateTopProducts(MarginCalculationResult calculationResult, ProductGroupingMode groupingMode, string? sortBy, bool sortDescending, string marginLevel)
    {
        var topProductsWithData = calculationResult.GroupTotals
            .Select(kvp =>
            {
                var displayName = _marginCalculator.GetGroupDisplayName(kvp.Key, groupingMode, calculationResult.GroupProducts[kvp.Key]);
                var products = calculationResult.GroupProducts[kvp.Key];

                // Calculate aggregated margin data for the group
                var groupData = CalculateGroupMarginData(products);

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
        var sortedProducts = ApplySorting(topProductsWithData, sortBy, sortDescending);

        // Add rank after sorting
        for (int i = 0; i < sortedProducts.Count; i++)
        {
            sortedProducts[i].Rank = i + 1;
        }

        return sortedProducts;
    }

    /// <summary>
    /// Calculates aggregated margin data for a group of products
    /// </summary>
    private GroupMarginData CalculateGroupMarginData(List<AnalyticsProduct> products)
    {
        if (!products.Any())
            return new GroupMarginData();

        // For groups, we calculate weighted averages based on sales volume
        var totalSales = products.Sum(p => p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C));

        if (totalSales == 0)
        {
            // If no sales, use simple average
            return new GroupMarginData
            {
                M0Amount = products.Average(p => p.M0Amount),
                M1Amount = products.Average(p => p.M1Amount),
                M2Amount = products.Average(p => p.M2Amount),
                M0Percentage = products.Average(p => p.M0Percentage),
                M1Percentage = products.Average(p => p.M1Percentage),
                M2Percentage = products.Average(p => p.M2Percentage),
                SellingPrice = products.Average(p => p.SellingPrice),
                PurchasePrice = products.Average(p => p.PurchasePrice)
            };
        }

        // Weighted average by sales volume
        return new GroupMarginData
        {
            M0Amount = products.Sum(p => p.M0Amount * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales,
            M1Amount = products.Sum(p => p.M1Amount * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales,
            M2Amount = products.Sum(p => p.M2Amount * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales,
            M0Percentage = products.Sum(p => p.M0Percentage * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales,
            M1Percentage = products.Sum(p => p.M1Percentage * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales,
            M2Percentage = products.Sum(p => p.M2Percentage * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales,
            SellingPrice = products.Sum(p => p.SellingPrice * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales,
            PurchasePrice = products.Sum(p => p.PurchasePrice * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales
        };
    }

    /// <summary>
    /// Applies sorting to the top products list
    /// </summary>
    private List<TopProductDto> ApplySorting(List<TopProductDto> products, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            // Default sorting by TotalMargin descending
            return sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList();
        }

        return sortBy.ToLower() switch
        {
            "groupkey" or "productcode" => sortDescending
                ? products.OrderByDescending(p => p.GroupKey).ToList()
                : products.OrderBy(p => p.GroupKey).ToList(),
            "displayname" or "productname" => sortDescending
                ? products.OrderByDescending(p => p.DisplayName).ToList()
                : products.OrderBy(p => p.DisplayName).ToList(),
            "totalmargin" => sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList(),
            // M0-M3 margin levels - amounts
            "m0amount" => sortDescending
                ? products.OrderByDescending(p => p.M0Amount).ToList()
                : products.OrderBy(p => p.M0Amount).ToList(),
            "m1amount" => sortDescending
                ? products.OrderByDescending(p => p.M1Amount).ToList()
                : products.OrderBy(p => p.M1Amount).ToList(),
            "m2amount" => sortDescending
                ? products.OrderByDescending(p => p.M2Amount).ToList()
                : products.OrderBy(p => p.M2Amount).ToList(),
            // M0-M2 margin levels - percentages
            "m0percentage" => sortDescending
                ? products.OrderByDescending(p => p.M0Percentage).ToList()
                : products.OrderBy(p => p.M0Percentage).ToList(),
            "m1percentage" => sortDescending
                ? products.OrderByDescending(p => p.M1Percentage).ToList()
                : products.OrderBy(p => p.M1Percentage).ToList(),
            "m2percentage" => sortDescending
                ? products.OrderByDescending(p => p.M2Percentage).ToList()
                : products.OrderBy(p => p.M2Percentage).ToList(),
            // Pricing
            "sellingprice" => sortDescending
                ? products.OrderByDescending(p => p.SellingPrice).ToList()
                : products.OrderBy(p => p.SellingPrice).ToList(),
            "purchaseprice" => sortDescending
                ? products.OrderByDescending(p => p.PurchasePrice).ToList()
                : products.OrderBy(p => p.PurchasePrice).ToList(),
            _ => sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList()
        };
    }

    /// <summary>
    /// Calculates total margin for a group of products based on selected margin level
    /// </summary>
    private decimal CalculateTotalMarginForLevel(List<AnalyticsProduct> products, string marginLevel)
    {
        var totalMargin = 0m;

        foreach (var product in products)
        {
            var totalSales = product.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C);

            var marginPerUnit = marginLevel.ToUpperInvariant() switch
            {
                "M0" => product.M0Amount,
                "M1" => product.M1Amount,
                "M2" => product.M2Amount,
                _ => product.M2Amount // Default to M2 (highest level now)
            };

            totalMargin += (decimal)totalSales * marginPerUnit;
        }

        return totalMargin;
    }

}

/// <summary>
/// Helper class for aggregated margin data calculation
/// </summary>
internal class GroupMarginData
{
    public decimal M0Amount { get; set; }
    public decimal M1Amount { get; set; }
    public decimal M2Amount { get; set; }
    public decimal M0Percentage { get; set; }
    public decimal M1Percentage { get; set; }
    public decimal M2Percentage { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal PurchasePrice { get; set; }
}