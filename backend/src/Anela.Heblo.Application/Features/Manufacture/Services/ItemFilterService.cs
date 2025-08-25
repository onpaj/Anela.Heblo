using Anela.Heblo.Application.Features.Manufacture.Model;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ItemFilterService : IItemFilterService
{
    public List<ManufacturingStockItemDto> FilterItems(
        List<ManufacturingStockItemDto> items,
        GetManufacturingStockAnalysisRequest request)
    {
        var filteredItems = items
            .Where(item => ShouldIncludeItem(item, request))
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var searchTerm = request.SearchTerm.ToLower();
            filteredItems = filteredItems
                .Where(i => i.Code.ToLower().Contains(searchTerm) ||
                           i.Name.ToLower().Contains(searchTerm) ||
                           (!string.IsNullOrEmpty(i.ProductFamily) && i.ProductFamily.ToLower().Contains(searchTerm)))
                .ToList();
        }

        return filteredItems;
    }

    public List<ManufacturingStockItemDto> SortItems(
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

    public ManufacturingStockSummaryDto CalculateSummary(
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
}