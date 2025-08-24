using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Handlers;

public class GetManufacturingStockAnalysisHandler : IRequestHandler<GetManufacturingStockAnalysisRequest, GetManufacturingStockAnalysisResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<GetManufacturingStockAnalysisHandler> _logger;

    public GetManufacturingStockAnalysisHandler(
        ICatalogRepository catalogRepository,
        ILogger<GetManufacturingStockAnalysisHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _logger = logger;
    }

    public async Task<GetManufacturingStockAnalysisResponse> Handle(
        GetManufacturingStockAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var (fromDate, toDate) = CalculateTimePeriod(request.TimePeriod, request.CustomFromDate, request.CustomToDate);

        var allCatalogItems = await _catalogRepository.GetAllAsync(cancellationToken);

        // Filter for finished products only - exclude raw materials and semi-products
        var finishedProducts = allCatalogItems
            .Where(item => item.Type == ProductType.Product)
            .ToList();

        // First, analyze ALL items for summary calculation
        var allAnalysisItems = new List<ManufacturingStockItemDto>();
        var productFamilies = new HashSet<string>();

        foreach (var item in finishedProducts)
        {
            var analysisItem = AnalyzeManufacturingStockItem(item, fromDate, toDate);
            allAnalysisItems.Add(analysisItem);

            if (!string.IsNullOrWhiteSpace(analysisItem.ProductFamily))
            {
                productFamilies.Add(analysisItem.ProductFamily);
            }
        }

        // Then filter items for display
        var analysisItems = allAnalysisItems
            .Where(item => ShouldIncludeItem(item, request))
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var searchTerm = request.SearchTerm.ToLower();
            analysisItems = analysisItems
                .Where(i => i.Code.ToLower().Contains(searchTerm) ||
                           i.Name.ToLower().Contains(searchTerm) ||
                           (!string.IsNullOrEmpty(i.ProductFamily) && i.ProductFamily.ToLower().Contains(searchTerm)))
                .ToList();
        }

        analysisItems = SortItems(analysisItems, request.SortBy, request.SortDescending);

        var totalCount = analysisItems.Count;
        var pagedItems = analysisItems
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        // Calculate summary from ALL items, not filtered ones
        var summary = CalculateSummary(allAnalysisItems, fromDate, toDate, productFamilies.OrderBy(x => x).ToList());

        return new GetManufacturingStockAnalysisResponse
        {
            Items = pagedItems,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            Summary = summary
        };
    }

    private (DateTime fromDate, DateTime toDate) CalculateTimePeriod(TimePeriodFilter timePeriod, DateTime? customFromDate, DateTime? customToDate)
    {
        var now = DateTime.UtcNow;

        switch (timePeriod)
        {
            case TimePeriodFilter.PreviousQuarter:
                // Last 3 completed months
                var startOfCurrentMonth = new DateTime(now.Year, now.Month, 1);
                var endOfPreviousMonth = startOfCurrentMonth.AddDays(-1);
                var startOfPreviousQuarter = startOfCurrentMonth.AddMonths(-3);
                return (startOfPreviousQuarter, endOfPreviousMonth);

            case TimePeriodFilter.FutureQuarter:
                // Next 3 months from previous year (for demand forecasting)
                var startOfFutureQuarterLastYear = new DateTime(now.Year - 1, now.Month, 1);
                var endOfFutureQuarterLastYear = startOfFutureQuarterLastYear.AddMonths(3).AddDays(-1);
                return (startOfFutureQuarterLastYear, endOfFutureQuarterLastYear);

            case TimePeriodFilter.Y2Y:
                // Last 12 months
                var startOfY2Y = new DateTime(now.Year, now.Month, 1).AddMonths(-12);
                var endOfY2Y = new DateTime(now.Year, now.Month, 1).AddDays(-1);
                return (startOfY2Y, endOfY2Y);

            case TimePeriodFilter.PreviousSeason:
                // October-January of previous year
                var seasonStart = new DateTime(now.Year - 1, 10, 1);
                var seasonEnd = new DateTime(now.Year, 1, 31);
                return (seasonStart, seasonEnd);

            case TimePeriodFilter.CustomPeriod:
                if (customFromDate.HasValue && customToDate.HasValue)
                {
                    return (customFromDate.Value, customToDate.Value);
                }
                goto default; // Fall back to default if custom dates not provided

            default:
                // Default to previous quarter
                var defaultStart = new DateTime(now.Year, now.Month, 1).AddMonths(-3);
                var defaultEnd = new DateTime(now.Year, now.Month, 1).AddDays(-1);
                return (defaultStart, defaultEnd);
        }
    }

    private ManufacturingStockItemDto AnalyzeManufacturingStockItem(CatalogAggregate item, DateTime fromDate, DateTime toDate)
    {
        var daysDiff = (toDate - fromDate).Days;
        if (daysDiff <= 0) daysDiff = 1;

        // For finished products, use sales data
        var salesInPeriod = item.GetTotalSold(fromDate, toDate);
        var dailySalesRate = salesInPeriod / (double)daysDiff;

        // Calculate stock days available
        var stockDaysAvailable = dailySalesRate > 0
            ? (double)item.Stock.Available / dailySalesRate
            : double.PositiveInfinity;

        // Get configuration from CatalogProperties
        var optimalStockDaysSetup = item.Properties.OptimalStockDaysSetup;
        var minStockSetup = item.Properties.StockMinSetup;
        var batchSize = item.Properties.BatchSize;

        // Calculate overstock percentage
        var overstockPercentage = optimalStockDaysSetup > 0
            ? (stockDaysAvailable / optimalStockDaysSetup) * 100
            : 0;

        // Determine severity based on business rules
        var severity = DetermineManufacturingSeverity(item, dailySalesRate, overstockPercentage);

        return new ManufacturingStockItemDto
        {
            Code = item.ProductCode,
            Name = item.ProductName,
            CurrentStock = (double)item.Stock.Available,
            SalesInPeriod = salesInPeriod,
            DailySalesRate = dailySalesRate,
            OptimalDaysSetup = optimalStockDaysSetup,
            StockDaysAvailable = double.IsInfinity(stockDaysAvailable) ? 999999 : stockDaysAvailable, // Cap infinity for display
            MinimumStock = (double)minStockSetup,
            OverstockPercentage = double.IsInfinity(overstockPercentage) ? 0 : overstockPercentage,
            BatchSize = batchSize.ToString(),
            ProductFamily = item.ProductFamily ?? string.Empty,
            Severity = severity,
            IsConfigured = item.Properties.OptimalStockDaysSetup > 0
        };
    }

    private ManufacturingStockSeverity DetermineManufacturingSeverity(
        CatalogAggregate catalogAggregate,
        double dailySalesRate,
        double overstockPercentage)
    {
        // Gray - Missing optimalStockDaysSetup configuration (Unconfigured)
        // Product MUST have optimalStockDaysSetup to be categorized as Critical/Adequate
        if (catalogAggregate.Properties.OptimalStockDaysSetup <= 0)
        {
            return ManufacturingStockSeverity.Unconfigured;
        }

        // Red - Overstock < 100% (Critical) - only for products with optimalStockDaysSetup > 0
        if (overstockPercentage < 100 && dailySalesRate > 0)
        {
            return ManufacturingStockSeverity.Critical;
        }

        // Orange - Below minimum stock (Major) - only for products with minStockSetup > 0
        if (catalogAggregate.Properties.StockMinSetup > 0 && catalogAggregate.Stock.Available < catalogAggregate.Properties.StockMinSetup)
        {
            return ManufacturingStockSeverity.Major;
        }

        // Green - All conditions OK (Adequate)
        return ManufacturingStockSeverity.Adequate;
    }

    private bool ShouldIncludeItem(ManufacturingStockItemDto item, GetManufacturingStockAnalysisRequest request)
    {
        // Product family filter
        if (!string.IsNullOrWhiteSpace(request.ProductFamily) &&
            !string.Equals(item.ProductFamily, request.ProductFamily, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Severity checkbox filters - if any severity filter is enabled, only show matching items
        bool hasAnySeverityFilter = request.CriticalItemsOnly || request.MajorItemsOnly || 
                                   request.AdequateItemsOnly || request.UnconfiguredOnly;
        
        if (hasAnySeverityFilter)
        {
            bool matchesSeverityFilter = false;
            
            if (request.CriticalItemsOnly && item.Severity == ManufacturingStockSeverity.Critical)
                matchesSeverityFilter = true;
                
            if (request.MajorItemsOnly && item.Severity == ManufacturingStockSeverity.Major)
                matchesSeverityFilter = true;
                
            if (request.AdequateItemsOnly && item.Severity == ManufacturingStockSeverity.Adequate)
                matchesSeverityFilter = true;
                
            if (request.UnconfiguredOnly && item.Severity == ManufacturingStockSeverity.Unconfigured)
                matchesSeverityFilter = true;
            
            if (!matchesSeverityFilter)
                return false;
        }
        else
        {
            // If no severity filters are active, hide unconfigured items by default
            if (item.Severity == ManufacturingStockSeverity.Unconfigured)
                return false;
        }

        return true;
    }

    private List<ManufacturingStockItemDto> SortItems(
        List<ManufacturingStockItemDto> items,
        ManufacturingStockSortBy sortBy,
        bool descending)
    {
        var sorted = sortBy switch
        {
            ManufacturingStockSortBy.ProductCode => items.OrderBy(i => i.Code),
            ManufacturingStockSortBy.ProductName => items.OrderBy(i => i.Name),
            ManufacturingStockSortBy.CurrentStock => items.OrderBy(i => i.CurrentStock),
            ManufacturingStockSortBy.SalesInPeriod => items.OrderBy(i => i.SalesInPeriod),
            ManufacturingStockSortBy.DailySales => items.OrderBy(i => i.DailySalesRate),
            ManufacturingStockSortBy.OptimalDaysSetup => items.OrderBy(i => i.OptimalDaysSetup),
            ManufacturingStockSortBy.StockDaysAvailable => items.OrderBy(i => i.StockDaysAvailable),
            ManufacturingStockSortBy.MinimumStock => items.OrderBy(i => i.MinimumStock),
            ManufacturingStockSortBy.OverstockPercentage => items.OrderBy(i => i.OverstockPercentage),
            ManufacturingStockSortBy.BatchSize => items.OrderBy(i => i.BatchSize),
            _ => items.OrderBy(i => i.StockDaysAvailable)
        };

        return descending ? sorted.Reverse().ToList() : sorted.ToList();
    }

    private ManufacturingStockSummaryDto CalculateSummary(
        List<ManufacturingStockItemDto> items,
        DateTime fromDate,
        DateTime toDate,
        List<string> productFamilies)
    {
        return new ManufacturingStockSummaryDto
        {
            TotalProducts = items.Count,
            CriticalCount = items.Count(i => i.Severity == ManufacturingStockSeverity.Critical),
            MajorCount = items.Count(i => i.Severity == ManufacturingStockSeverity.Major),
            MinorCount = items.Count(i => i.Severity == ManufacturingStockSeverity.Minor),
            AdequateCount = items.Count(i => i.Severity == ManufacturingStockSeverity.Adequate),
            UnconfiguredCount = items.Count(i => i.Severity == ManufacturingStockSeverity.Unconfigured),
            AnalysisPeriodStart = fromDate,
            AnalysisPeriodEnd = toDate,
            ProductFamilies = productFamilies
        };
    }
}