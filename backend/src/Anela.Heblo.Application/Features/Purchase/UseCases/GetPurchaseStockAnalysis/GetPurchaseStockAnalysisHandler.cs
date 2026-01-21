using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Xcc;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;

public class GetPurchaseStockAnalysisHandler : IRequestHandler<GetPurchaseStockAnalysisRequest, GetPurchaseStockAnalysisResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IStockSeverityCalculator _stockSeverityCalculator;
    private readonly ILogger<GetPurchaseStockAnalysisHandler> _logger;

    public GetPurchaseStockAnalysisHandler(
        ICatalogRepository catalogRepository,
        IStockSeverityCalculator stockSeverityCalculator,
        ILogger<GetPurchaseStockAnalysisHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _stockSeverityCalculator = stockSeverityCalculator;
        _logger = logger;
    }

    public async Task<GetPurchaseStockAnalysisResponse> Handle(
        GetPurchaseStockAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var fromDate = request.FromDate ?? DateTime.UtcNow.AddYears(-1);
        var toDate = request.ToDate ?? DateTime.UtcNow;

        if (fromDate > toDate)
        {
            _logger.LogWarning("Invalid date range: FromDate {FromDate} is after ToDate {ToDate}", fromDate, toDate);
            return new GetPurchaseStockAnalysisResponse(ErrorCodes.InvalidDateRange, new Dictionary<string, string> { { "FromDate", fromDate.ToString() }, { "ToDate", toDate.ToString() } });
        }

        var allCatalogItems = await _catalogRepository.GetAllAsync(cancellationToken);

        var filteredItems = allCatalogItems
            .Where(item => item.Type == ProductType.Material || item.Type == ProductType.Goods)
            .ToList();

        // First, analyze ALL items for summary calculation
        var allAnalysisItems = new List<StockAnalysisItemDto>();
        foreach (var item in filteredItems)
        {
            var analysisItem = AnalyzeStockItem(item, fromDate, toDate);
            allAnalysisItems.Add(analysisItem);
        }

        // Then filter items for display
        var analysisItems = allAnalysisItems
            .Where(item => ShouldIncludeItem(item, request))
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var searchTerm = request.SearchTerm.ToLower();
            var normalizedSearchTerm = request.SearchTerm.Trim().NormalizeForSearch();
            analysisItems = analysisItems
                .Where(i => i.ProductCode.ToLower().Contains(searchTerm) ||
                           i.ProductNameNormalized.Contains(normalizedSearchTerm) ||
                           (i.Supplier != null && i.Supplier.Contains(searchTerm, StringComparison.InvariantCultureIgnoreCase)) ||
                           (i.LastPurchase?.SupplierName?.ToLower().Contains(searchTerm) ?? false))
                .ToList();
        }

        analysisItems = SortItems(analysisItems, request.SortBy, request.SortDescending);

        var totalCount = analysisItems.Count;
        var pagedItems = analysisItems
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        // Calculate summary from ALL items, not filtered ones
        var summary = CalculateSummary(allAnalysisItems, fromDate, toDate);

        return new GetPurchaseStockAnalysisResponse
        {
            Items = pagedItems,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            Summary = summary
        };
    }

    private StockAnalysisItemDto AnalyzeStockItem(CatalogAggregate item, DateTime fromDate, DateTime toDate)
    {
        var daysDiff = (toDate - fromDate).Days;
        if (daysDiff <= 0) daysDiff = 1;

        double consumption = 0;
        if (item.Type == ProductType.Material)
        {
            consumption = item.GetConsumed(fromDate, toDate);
        }
        else if (item.Type == ProductType.Goods)
        {
            consumption = item.GetTotalSold(fromDate, toDate);
        }

        var dailyConsumption = consumption / (double)daysDiff;

        int? daysUntilStockout = null;
        if (dailyConsumption > 0)
        {
            daysUntilStockout = (int)((double)item.Stock.EffectiveStock / dailyConsumption);
        }

        var minStock = item.Properties.StockMinSetup;
        var optimalStockDays = item.Properties.OptimalStockDaysSetup;
        var optimalStock = optimalStockDays > 0 ? dailyConsumption * (double)optimalStockDays : 0;

        var stockEfficiency = CalculateStockEfficiency((double)item.Stock.EffectiveStock, (double)minStock, optimalStock);
        var severity = _stockSeverityCalculator.DetermineStockSeverity((double)item.Stock.EffectiveStock, (double)minStock, optimalStock, item.IsMinStockConfigured, item.IsOptimalStockConfigured);

        var lastPurchase = GetLastPurchaseInfo(item);

        var recommendedQuantity = CalculateRecommendedOrderQuantity(
            (double)item.Stock.Available,
            optimalStock,
            (double)minStock,
            item.MinimalOrderQuantity);

        return new StockAnalysisItemDto
        {
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            ProductNameNormalized = item.ProductNameNormalized,
            ProductType = item.Type.ToString(),
            AvailableStock = (double)item.Stock.Available,
            OrderedStock = (double)item.Stock.Ordered,
            EffectiveStock = (double)item.Stock.EffectiveStock,
            MinStockLevel = (double)minStock,
            OptimalStockLevel = optimalStock,
            ConsumptionInPeriod = consumption,
            DailyConsumption = dailyConsumption,
            DaysUntilStockout = daysUntilStockout,
            StockEfficiencyPercentage = stockEfficiency,
            Severity = severity,
            MinimalOrderQuantity = item.MinimalOrderQuantity,
            LastPurchase = lastPurchase,
            Supplier = item.SupplierName,
            RecommendedOrderQuantity = recommendedQuantity,
            IsConfigured = item.IsMinStockConfigured || item.IsOptimalStockConfigured
        };
    }

    private double CalculateStockEfficiency(double availableStock, double minStock, double optimalStock)
    {
        if (optimalStock <= 0)
        {
            return minStock > 0 ? (availableStock / minStock) * 100 : 0;
        }

        return (availableStock / optimalStock) * 100;
    }


    private LastPurchaseInfoDto? GetLastPurchaseInfo(CatalogAggregate item)
    {
        var lastPurchase = item.PurchaseHistory
            .OrderByDescending(p => p.Date)
            .FirstOrDefault();

        if (lastPurchase == null)
        {
            return null;
        }

        return new LastPurchaseInfoDto
        {
            Date = lastPurchase.Date,
            SupplierName = lastPurchase.SupplierName ?? string.Empty,
            Amount = lastPurchase.Amount,
            UnitPrice = lastPurchase.PricePerPiece,
            TotalPrice = lastPurchase.PriceTotal
        };
    }

    private double? CalculateRecommendedOrderQuantity(double availableStock, double optimalStock, double minStock, string moq)
    {
        if (optimalStock <= 0 && minStock <= 0)
        {
            return null;
        }

        var targetStock = optimalStock > 0 ? optimalStock : minStock * 2;
        var needed = targetStock - availableStock;

        if (needed <= 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(moq) && double.TryParse(moq, out var minOrderQty))
        {
            return Math.Max(needed, minOrderQty);
        }

        return needed;
    }

    private bool ShouldIncludeItem(StockAnalysisItemDto item, GetPurchaseStockAnalysisRequest request)
    {
        if (request.OnlyConfigured && !item.IsConfigured)
        {
            return false;
        }

        return request.StockStatus switch
        {
            StockStatusFilter.Critical => item.Severity == StockSeverity.Critical,
            StockStatusFilter.Low => item.Severity == StockSeverity.Low,
            StockStatusFilter.Optimal => item.Severity == StockSeverity.Optimal,
            StockStatusFilter.Overstocked => item.Severity == StockSeverity.Overstocked,
            StockStatusFilter.NotConfigured => item.Severity == StockSeverity.NotConfigured,
            _ => true
        };
    }

    private List<StockAnalysisItemDto> SortItems(List<StockAnalysisItemDto> items, StockAnalysisSortBy sortBy, bool descending)
    {
        var sorted = sortBy switch
        {
            StockAnalysisSortBy.ProductCode => items.OrderBy(i => i.ProductCode),
            StockAnalysisSortBy.ProductName => items.OrderBy(i => i.ProductName),
            StockAnalysisSortBy.AvailableStock => items.OrderBy(i => i.AvailableStock),
            StockAnalysisSortBy.Consumption => items.OrderBy(i => i.ConsumptionInPeriod),
            StockAnalysisSortBy.StockEfficiency => items.OrderBy(i => i.StockEfficiencyPercentage),
            StockAnalysisSortBy.LastPurchaseDate => items.OrderBy(i => i.LastPurchase?.Date ?? DateTime.MinValue),
            _ => items.OrderBy(i => i.StockEfficiencyPercentage)
        };

        return descending ? sorted.Reverse().ToList() : sorted.ToList();
    }

    private StockAnalysisSummaryDto CalculateSummary(List<StockAnalysisItemDto> items, DateTime fromDate, DateTime toDate)
    {
        return new StockAnalysisSummaryDto
        {
            TotalProducts = items.Count,
            CriticalCount = items.Count(i => i.Severity == StockSeverity.Critical),
            LowStockCount = items.Count(i => i.Severity == StockSeverity.Low),
            OptimalCount = items.Count(i => i.Severity == StockSeverity.Optimal),
            OverstockedCount = items.Count(i => i.Severity == StockSeverity.Overstocked),
            NotConfiguredCount = items.Count(i => i.Severity == StockSeverity.NotConfigured),
            TotalInventoryValue = items.Sum(i => (decimal)i.EffectiveStock * (i.LastPurchase?.UnitPrice ?? 0)),
            AnalysisPeriodStart = fromDate,
            AnalysisPeriodEnd = toDate
        };
    }
}