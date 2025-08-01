using MediatR;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.features.catalog.contracts;
using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.Domain.Catalog.Sales;
using Anela.Heblo.Application.Domain.Catalog.PurchaseHistory;
using Anela.Heblo.Application.Domain.Catalog.ConsumedMaterials;
using AutoMapper;

namespace Anela.Heblo.Application.Features.Catalog.Application;

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
        var salesHistory = GetSalesHistoryFromAggregate(catalogItem);
        var purchaseHistory = GetPurchaseHistoryFromAggregate(catalogItem);
        var consumedHistory = GetConsumedHistoryFromAggregate(catalogItem);

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

    private List<CatalogSalesRecordDto> GetSalesHistoryFromAggregate(CatalogAggregate catalogItem)
    {
        // Get data from the already loaded aggregate (from cache)
        var salesData = catalogItem.SalesHistory ?? new List<CatalogSaleRecord>();
        
        // Filter to last 13 months and group by month and year for optimization
        var currentDate = _timeProvider.GetUtcNow().Date;
        var fromDate = currentDate.AddMonths(-13);
        
        return salesData
            .Where(s => s.Date >= fromDate)
            .GroupBy(s => new { s.Date.Year, s.Date.Month })
            .Select(g => new CatalogSalesRecordDto
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                AmountTotal = g.Sum(s => s.AmountTotal),
                AmountB2B = g.Sum(s => s.AmountB2B),
                AmountB2C = g.Sum(s => s.AmountB2C),
                SumTotal = g.Sum(s => s.SumTotal),
                SumB2B = g.Sum(s => s.SumB2B),
                SumB2C = g.Sum(s => s.SumB2C)
            }).ToList();
    }

    private List<CatalogPurchaseRecordDto> GetPurchaseHistoryFromAggregate(CatalogAggregate catalogItem)
    {
        // Get data from the already loaded aggregate (from cache)
        var purchaseData = catalogItem.PurchaseHistory ?? new List<CatalogPurchaseRecord>();
        
        // Filter to last 13 months and group by month and year for optimization
        var currentDate = _timeProvider.GetUtcNow().Date;
        var fromDate = currentDate.AddMonths(-13);
        
        return purchaseData
            .Where(p => p.Date >= fromDate)
            .GroupBy(p => new { p.Date.Year, p.Date.Month })
            .Select(g => new CatalogPurchaseRecordDto
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                SupplierName = g.First().SupplierName ?? string.Empty, // Take first supplier for the month
                Amount = g.Sum(p => p.Amount),
                PricePerPiece = g.Count() > 0 ? g.Average(p => p.PricePerPiece) : 0, // Average price per piece
                PriceTotal = g.Sum(p => p.PriceTotal),
                DocumentNumber = string.Join(", ", g.Select(p => p.DocumentNumber).Where(d => !string.IsNullOrEmpty(d)).Distinct())
            }).ToList();
    }

    private List<CatalogConsumedRecordDto> GetConsumedHistoryFromAggregate(CatalogAggregate catalogItem)
    {
        // Get data from the already loaded aggregate (from cache)
        var consumedData = catalogItem.ConsumedHistory ?? new List<ConsumedMaterialRecord>();
        
        // Filter to last 13 months and group by month and year for optimization
        var currentDate = _timeProvider.GetUtcNow().Date;
        var fromDate = currentDate.AddMonths(-13);
        
        return consumedData
            .Where(c => c.Date >= fromDate)
            .GroupBy(c => new { c.Date.Year, c.Date.Month })
            .Select(g => new CatalogConsumedRecordDto
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                Amount = g.Sum(c => c.Amount),
                ProductName = g.First().ProductName ?? string.Empty
            }).ToList();
    }
}