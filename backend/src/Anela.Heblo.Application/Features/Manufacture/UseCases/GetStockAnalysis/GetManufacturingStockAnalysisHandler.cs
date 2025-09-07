using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;

public class GetManufacturingStockAnalysisHandler : IRequestHandler<GetManufacturingStockAnalysisRequest, GetManufacturingStockAnalysisResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ITimePeriodCalculator _timePeriodCalculator;
    private readonly IConsumptionRateCalculator _consumptionCalculator;
    private readonly IProductionActivityAnalyzer _productionAnalyzer;
    private readonly IManufactureSeverityCalculator _severityCalculator;
    private readonly IManufactureAnalysisMapper _mapper;
    private readonly IItemFilterService _filterService;
    private readonly ILogger<GetManufacturingStockAnalysisHandler> _logger;

    public GetManufacturingStockAnalysisHandler(
        ICatalogRepository catalogRepository,
        ITimePeriodCalculator timePeriodCalculator,
        IConsumptionRateCalculator consumptionCalculator,
        IProductionActivityAnalyzer productionAnalyzer,
        IManufactureSeverityCalculator severityCalculator,
        IManufactureAnalysisMapper mapper,
        IItemFilterService filterService,
        ILogger<GetManufacturingStockAnalysisHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _timePeriodCalculator = timePeriodCalculator;
        _consumptionCalculator = consumptionCalculator;
        _productionAnalyzer = productionAnalyzer;
        _severityCalculator = severityCalculator;
        _mapper = mapper;
        _filterService = filterService;
        _logger = logger;
    }

    public async Task<GetManufacturingStockAnalysisResponse> Handle(
        GetManufacturingStockAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Calculate time period
        var (fromDate, toDate) = _timePeriodCalculator.CalculateTimePeriod(
            request.TimePeriod, request.CustomFromDate, request.CustomToDate);

        // 2. Get finished products data
        var allCatalogItems = await _catalogRepository.GetAllAsync(cancellationToken);
        var finishedProducts = allCatalogItems
            .Where(item => item.Type == ProductType.Product)
            .ToList();

        if (!finishedProducts.Any())
        {
            _logger.LogWarning("No finished products found in catalog for manufacturing stock analysis");
            return new GetManufacturingStockAnalysisResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ManufacturingDataNotAvailable,
                Params = new Dictionary<string, string> { ["reason"] = "No finished products available for analysis" }
            };
        }

        // 3. Analyze all items for summary calculation
        var allAnalysisItems = finishedProducts
            .Select(item => AnalyzeManufacturingStockItem(item, fromDate, toDate))
            .ToList();

        var productFamilies = allAnalysisItems
            .Where(item => !string.IsNullOrWhiteSpace(item.ProductFamily))
            .Select(item => item.ProductFamily!)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        // 4. Filter and sort items for display
        var filteredItems = _filterService.FilterItems(allAnalysisItems, request);
        var sortedItems = _filterService.SortItems(filteredItems, request.SortBy, request.SortDescending);

        // 5. Apply pagination
        var totalCount = sortedItems.Count;
        var pagedItems = sortedItems
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        // 6. Calculate summary from all items
        var summary = _filterService.CalculateSummary(allAnalysisItems, fromDate, toDate, productFamilies);

        return new GetManufacturingStockAnalysisResponse
        {
            Items = pagedItems,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            Summary = summary
        };
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
}