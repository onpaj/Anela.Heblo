using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Handlers;

public class GetManufacturingStockAnalysisHandler : IRequestHandler<GetManufacturingStockAnalysisRequest, GetManufacturingStockAnalysisResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IConsumptionRateCalculator _consumptionCalculator;
    private readonly IProductionActivityAnalyzer _productionAnalyzer;
    private readonly IManufactureSeverityCalculator _severityCalculator;
    private readonly IManufactureAnalysisMapper _mapper;
    private readonly ILogger<GetManufacturingStockAnalysisHandler> _logger;

    public GetManufacturingStockAnalysisHandler(
        ICatalogRepository catalogRepository,
        IConsumptionRateCalculator consumptionCalculator,
        IProductionActivityAnalyzer productionAnalyzer,
        IManufactureSeverityCalculator severityCalculator,
        IManufactureAnalysisMapper mapper,
        ILogger<GetManufacturingStockAnalysisHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _consumptionCalculator = consumptionCalculator;
        _productionAnalyzer = productionAnalyzer;
        _severityCalculator = severityCalculator;
        _mapper = mapper;
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
        // Calculate daily sales rate using domain service
        var dailySalesRate = _consumptionCalculator.CalculateDailySalesRate(item.SalesHistory, fromDate, toDate);
        
        // Calculate total sales in period for display
        var salesInPeriod = item.GetTotalSold(fromDate, toDate);

        // Calculate stock days available using domain service
        var stockDaysAvailable = _consumptionCalculator.CalculateStockDaysAvailable(item.Stock.Available, dailySalesRate);

        // Calculate overstock percentage using domain service
        var overstockPercentage = _severityCalculator.CalculateOverstockPercentage(stockDaysAvailable, item.Properties.OptimalStockDaysSetup);

        // Determine severity using domain service
        var severity = _severityCalculator.CalculateSeverity(item, dailySalesRate, stockDaysAvailable);

        // Check if product is in active production using domain service
        var isInProduction = _productionAnalyzer.IsInActiveProduction(item.ManufactureHistory);

        // Map to DTO using domain service
        return _mapper.MapToDto(
            item,
            severity,
            dailySalesRate,
            salesInPeriod,
            stockDaysAvailable,
            overstockPercentage,
            isInProduction);
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