using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Xcc;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;

public class GetPurchaseStockAnalysisHandler : IRequestHandler<GetPurchaseStockAnalysisRequest, GetPurchaseStockAnalysisResponse>
{
    private readonly IMaterialCatalogService _materialCatalog;
    private readonly IStockAnalysisCalculator _stockAnalysisCalculator;
    private readonly ILogger<GetPurchaseStockAnalysisHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public GetPurchaseStockAnalysisHandler(
        IMaterialCatalogService materialCatalog,
        IStockAnalysisCalculator stockAnalysisCalculator,
        ILogger<GetPurchaseStockAnalysisHandler> logger,
        TimeProvider timeProvider)
    {
        _materialCatalog = materialCatalog;
        _stockAnalysisCalculator = stockAnalysisCalculator;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<GetPurchaseStockAnalysisResponse> Handle(
        GetPurchaseStockAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var fromDate = request.FromDate ?? now.AddYears(-1);
        var toDate = request.ToDate ?? now;

        if (fromDate > toDate)
        {
            _logger.LogWarning("Invalid date range: FromDate {FromDate} is after ToDate {ToDate}", fromDate, toDate);
            return new GetPurchaseStockAnalysisResponse(ErrorCodes.InvalidDateRange, new Dictionary<string, string> { { "FromDate", fromDate.ToString() }, { "ToDate", toDate.ToString() } });
        }

        var snapshots = await _materialCatalog.GetStockAnalysisSnapshotsAsync(fromDate, toDate, cancellationToken);

        // First, analyze ALL items of the selected material category for summary calculation
        var allAnalysisItems = snapshots
            .Where(s => MaterialCategoryResolver.Matches(s.ProductCode, request.MaterialCategory))
            .Select(s => _stockAnalysisCalculator.AnalyzeItem(s, fromDate, toDate))
            .ToList();

        // Then filter items for display
        var analysisItems = _stockAnalysisCalculator.FilterItems(allAnalysisItems, request);

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
        var pagedItems = request.IsExport
            ? analysisItems
            : analysisItems
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