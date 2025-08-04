using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Domain.Features.Catalog;
using AutoMapper;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog;

public class GetCatalogDetailHandler : IRequestHandler<GetCatalogDetailRequest, GetCatalogDetailResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;

    public GetCatalogDetailHandler(
        ICatalogRepository catalogRepository,
        IMapper mapper,
        TimeProvider timeProvider)
    {
        _catalogRepository = catalogRepository;
        _mapper = mapper;
        _timeProvider = timeProvider;
    }

    public async Task<GetCatalogDetailResponse> Handle(GetCatalogDetailRequest request, CancellationToken cancellationToken)
    {
        // Get catalog item with all historical data already loaded from cache
        var catalogItem = await _catalogRepository.SingleOrDefaultAsync(
            x => x.ProductCode == request.ProductCode,
            cancellationToken);

        if (catalogItem == null)
        {
            throw new InvalidOperationException($"Product with code '{request.ProductCode}' not found.");
        }

        // Convert historical data to DTOs with monthly grouping
        var salesHistory = GetSalesHistoryFromAggregate(catalogItem, request.MonthsBack);
        var purchaseHistory = GetPurchaseHistoryFromAggregate(catalogItem, request.MonthsBack);
        var consumedHistory = GetConsumedHistoryFromAggregate(catalogItem, request.MonthsBack);

        return new GetCatalogDetailResponse
        {
            Item = _mapper.Map<CatalogItemDto>(catalogItem),
            HistoricalData = new CatalogHistoricalDataDto
            {
                SalesHistory = salesHistory.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToList(),
                PurchaseHistory = purchaseHistory.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToList(),
                ConsumedHistory = consumedHistory.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToList()
            }
        };
    }

    private List<CatalogSalesRecordDto> GetSalesHistoryFromAggregate(CatalogAggregate catalogItem, int monthsBack)
    {
        // Use pre-calculated summary data - much faster than runtime aggregation
        var currentDate = _timeProvider.GetUtcNow().Date;
        var fromDate = currentDate.AddMonths(-monthsBack);
        var fromKey = $"{fromDate.Year:D4}-{fromDate.Month:D2}";

        return catalogItem.SaleHistorySummary.MonthlyData
            .Where(kvp => string.Compare(kvp.Key, fromKey, StringComparison.Ordinal) >= 0)
            .Select(kvp => new CatalogSalesRecordDto
            {
                Year = kvp.Value.Year,
                Month = kvp.Value.Month,
                AmountTotal = kvp.Value.TotalAmount,
                AmountB2B = kvp.Value.AmountB2B,
                AmountB2C = kvp.Value.AmountB2C,
                SumTotal = kvp.Value.TotalRevenue,
                SumB2B = kvp.Value.TotalB2B,
                SumB2C = kvp.Value.TotalB2C
            }).ToList();
    }

    private List<CatalogPurchaseRecordDto> GetPurchaseHistoryFromAggregate(CatalogAggregate catalogItem, int monthsBack)
    {
        // Return individual purchase records instead of monthly summaries
        var currentDate = _timeProvider.GetUtcNow().Date;
        var fromDate = currentDate.AddMonths(-monthsBack);

        return catalogItem.PurchaseHistory
            .Where(p => p.Date >= fromDate)
            .OrderByDescending(p => p.Date)
            .Select(p => new CatalogPurchaseRecordDto
            {
                Date = p.Date,
                SupplierName = p.SupplierName,
                Amount = p.Amount,
                PricePerPiece = p.PricePerPiece,
                PriceTotal = p.PriceTotal,
                DocumentNumber = p.DocumentNumber
            }).ToList();
    }

    private List<CatalogConsumedRecordDto> GetConsumedHistoryFromAggregate(CatalogAggregate catalogItem, int monthsBack)
    {
        // Use pre-calculated summary data - much faster than runtime aggregation
        var currentDate = _timeProvider.GetUtcNow().Date;
        var fromDate = currentDate.AddMonths(-monthsBack);
        var fromKey = $"{fromDate.Year:D4}-{fromDate.Month:D2}";

        return catalogItem.ConsumedHistorySummary.MonthlyData
            .Where(kvp => string.Compare(kvp.Key, fromKey, StringComparison.Ordinal) >= 0)
            .Select(kvp => new CatalogConsumedRecordDto
            {
                Year = kvp.Value.Year,
                Month = kvp.Value.Month,
                Amount = kvp.Value.TotalAmount,
                ProductName = catalogItem.ProductName // Use product name from main aggregate
            }).ToList();
    }
}