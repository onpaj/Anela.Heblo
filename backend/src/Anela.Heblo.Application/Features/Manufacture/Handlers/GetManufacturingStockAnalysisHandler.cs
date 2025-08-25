using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Handlers;

public class GetManufacturingStockAnalysisHandler : IRequestHandler<GetManufacturingStockAnalysisRequest, GetManufacturingStockAnalysisResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ITimePeriodCalculator _timePeriodCalculator;
    private readonly IManufactureAnalysisMapper _mapper;
    private readonly IManufactureSeverityCalculator _severityCalculator;
    private readonly IItemFilterService _filterService;
    private readonly ILogger<GetManufacturingStockAnalysisHandler> _logger;

    public GetManufacturingStockAnalysisHandler(
        ICatalogRepository catalogRepository,
        ITimePeriodCalculator timePeriodCalculator,
        IManufactureAnalysisMapper mapper,
        IManufactureSeverityCalculator severityCalculator,
        IItemFilterService filterService,
        ILogger<GetManufacturingStockAnalysisHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _timePeriodCalculator = timePeriodCalculator;
        _mapper = mapper;
        _severityCalculator = severityCalculator;
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
        var (_, dailySalesRate, _, overstockPercentage) = _mapper.CalculateStockMetrics(item, fromDate, toDate);
        var severity = _severityCalculator.CalculateSeverity(item, dailySalesRate, overstockPercentage);
        return _mapper.MapToDto(item, fromDate, toDate, severity);
    }
}