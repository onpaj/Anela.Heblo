using System.Globalization;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.Handlers;

public class GetProductMarginSummaryHandler : IRequestHandler<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IProductMarginAnalysisService _marginAnalysisService;

    private const string OtherColor = "#9CA3AF"; // Gray

    public GetProductMarginSummaryHandler(
        ICatalogRepository catalogRepository,
        IProductMarginAnalysisService marginAnalysisService)
    {
        _catalogRepository = catalogRepository;
        _marginAnalysisService = marginAnalysisService;
    }

    public async Task<GetProductMarginSummaryResponse> Handle(GetProductMarginSummaryRequest request, CancellationToken cancellationToken)
    {
        // 1. Parse time window and calculate date range
        var (fromDate, toDate) = _marginAnalysisService.ParseTimeWindow(request.TimeWindow);

        // 2. Get all products with Product/Goods types that have sales in the period  
        var productTypes = new[] { ProductType.Product, ProductType.Goods };
        var products = await _catalogRepository.GetProductsWithSalesInPeriod(fromDate, toDate, productTypes, cancellationToken);

        // 3. Calculate total margin per group across entire period
        var groupMarginMap = _marginAnalysisService.CalculateGroupTotalMargin(products, fromDate, toDate, request.GroupingMode);

        // 4. Get ALL groups by margin (highest first) - frontend will handle limiting for chart
        var allGroups = GetAllGroupsWithColors(groupMarginMap, products, request.GroupingMode);

        // 5. Generate monthly breakdown with ALL group segments (no "Other" category)
        var monthlyData = GenerateMonthlyBreakdown(products, fromDate, toDate, request.GroupingMode);

        return new GetProductMarginSummaryResponse
        {
            MonthlyData = monthlyData,
            TopProducts = allGroups,
            TotalMargin = groupMarginMap.Values.Sum(),
            TimeWindow = request.TimeWindow,
            GroupingMode = request.GroupingMode,
            FromDate = fromDate,
            ToDate = toDate
        };
    }

    private List<TopProductDto> GetAllGroupsWithColors(
        Dictionary<string, decimal> groupMarginMap,
        List<CatalogAggregate> products,
        ProductGroupingMode groupingMode)
    {
        var allGroups = groupMarginMap
            .OrderByDescending(kvp => kvp.Value)
            .Select((kvp, index) =>
            {
                var displayName = _marginAnalysisService.GetGroupDisplayName(kvp.Key, groupingMode, products);
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

        return allGroups;
    }

    private List<MonthlyProductMarginDto> GenerateMonthlyBreakdown(
        List<CatalogAggregate> products,
        DateTime fromDate,
        DateTime toDate,
        ProductGroupingMode groupingMode)
    {
        var monthlyData = new List<MonthlyProductMarginDto>();

        // Generate all months in the date range
        var current = new DateTime(fromDate.Year, fromDate.Month, 1);
        var end = new DateTime(toDate.Year, toDate.Month, 1);

        while (current <= end)
        {
            var monthStart = current;
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            // Group products by their group key for this month
            var monthlyGroupData = new Dictionary<string, GroupAggregateData>();
            var totalMonthMargin = 0m;

            foreach (var product in products)
            {
                var salesInMonth = product.SalesHistory
                    .Where(s => s.Date >= monthStart && s.Date <= monthEnd)
                    .ToList();

                if (!salesInMonth.Any() || product.MarginAmount <= 0)
                    continue;

                var groupKey = _marginAnalysisService.GetGroupKey(product, groupingMode);
                var unitsSold = (int)salesInMonth.Sum(s => s.AmountB2B + s.AmountB2C);
                var marginContribution = unitsSold * product.MarginAmount;
                totalMonthMargin += marginContribution;

                if (!monthlyGroupData.ContainsKey(groupKey))
                {
                    monthlyGroupData[groupKey] = new GroupAggregateData();
                }

                var groupData = monthlyGroupData[groupKey];
                groupData.TotalMargin += marginContribution;
                groupData.TotalUnitsSold += unitsSold;
                groupData.Products.Add(product);
                groupData.ProductSales.Add((product, unitsSold));
            }

            // Create segments for ALL groups that have data in this month
            var segments = new List<ProductMarginSegmentDto>();
            
            foreach (var groupKey in monthlyGroupData.Keys)
            {
                var groupData = monthlyGroupData[groupKey];
                var displayName = _marginAnalysisService.GetGroupDisplayName(groupKey, groupingMode, products);
                
                // Calculate averages for the group
                var avgMarginPerPiece = groupData.Products.Count > 0 ? 
                    groupData.Products.Average(p => p.MarginAmount) : 0;
                var avgSellingPrice = groupData.Products.Count > 0 ? 
                    groupData.Products.Average(p => p.EshopPrice?.PriceWithoutVat ?? 0) : 0;
                var avgMaterialCosts = groupData.Products.Count > 0 ?
                    groupData.Products.Average(p => _marginAnalysisService.CalculateMaterialCosts(p)) : 0;
                var avgLaborCosts = groupData.Products.Count > 0 ?
                    groupData.Products.Average(p => _marginAnalysisService.CalculateLaborCosts(p)) : 0;

                segments.Add(new ProductMarginSegmentDto
                {
                    GroupKey = groupKey,
                    DisplayName = displayName,
                    MarginContribution = groupData.TotalMargin,
                    ColorCode = "", // Color will be assigned on frontend
                    AverageMarginPerPiece = avgMarginPerPiece,
                    UnitsSold = groupData.TotalUnitsSold,
                    AverageSellingPriceWithoutVat = avgSellingPrice,
                    AverageMaterialCosts = avgMaterialCosts,
                    AverageLaborCosts = avgLaborCosts,
                    ProductCount = groupData.Products.Count,
                    IsOther = false
                });
            }

            // Sort segments by margin contribution in THIS month (highest first)
            segments = segments.OrderByDescending(s => s.MarginContribution).ToList();

            // Calculate percentages
            if (totalMonthMargin > 0)
            {
                foreach (var segment in segments)
                {
                    segment.Percentage = (segment.MarginContribution / totalMonthMargin) * 100;
                }
            }

            monthlyData.Add(new MonthlyProductMarginDto
            {
                Year = current.Year,
                Month = current.Month,
                MonthDisplay = current.ToString("MMM yyyy", CultureInfo.CreateSpecificCulture("cs-CZ")),
                ProductSegments = segments,
                TotalMonthMargin = totalMonthMargin
            });

            current = current.AddMonths(1);
        }

        return monthlyData;
    }

    private class GroupAggregateData
    {
        public decimal TotalMargin { get; set; }
        public int TotalUnitsSold { get; set; }
        public List<CatalogAggregate> Products { get; set; } = new();
        public List<(CatalogAggregate Product, int UnitsSold)> ProductSales { get; set; } = new();
    }
}