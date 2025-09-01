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
        var manufactureHistory = GetManufactureHistoryFromAggregate(catalogItem, request.MonthsBack);
        var manufactureCostHistory = GetManufactureCostHistoryFromAggregate(catalogItem, request.MonthsBack);
        var marginHistory = GetMarginHistoryFromAggregate(catalogItem, request.MonthsBack);

        return new GetCatalogDetailResponse
        {
            Item = _mapper.Map<CatalogItemDto>(catalogItem),
            HistoricalData = new CatalogHistoricalDataDto
            {
                SalesHistory = salesHistory.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToList(),
                PurchaseHistory = purchaseHistory.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToList(),
                ConsumedHistory = consumedHistory.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToList(),
                ManufactureHistory = manufactureHistory.OrderByDescending(x => x.Date).ToList(),
                ManufactureCostHistory = manufactureCostHistory.OrderByDescending(x => x.Date).ToList(),
                MarginHistory = marginHistory.OrderByDescending(x => x.Date).ToList()
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

        // For very high monthsBack values (like ALL_HISTORY_MONTHS_THRESHOLD), return all records without date filtering
        // to avoid potential issues with very old dates
        if (monthsBack >= CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
        {
            return catalogItem.PurchaseHistory
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

    private List<CatalogManufactureRecordDto> GetManufactureHistoryFromAggregate(CatalogAggregate catalogItem, int monthsBack)
    {
        // Return individual manufacture records instead of monthly summaries
        var currentDate = _timeProvider.GetUtcNow().Date;

        // For very high monthsBack values (like ALL_HISTORY_MONTHS_THRESHOLD), return all records without date filtering
        // to avoid potential issues with very old dates
        if (monthsBack >= CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
        {
            return catalogItem.ManufactureHistory
                .OrderByDescending(m => m.Date)
                .Select(m => new CatalogManufactureRecordDto
                {
                    Date = m.Date,
                    Amount = m.Amount,
                    PricePerPiece = m.PricePerPiece,
                    PriceTotal = m.PriceTotal,
                    ProductCode = m.ProductCode,
                    DocumentNumber = m.DocumentNumber
                }).ToList();
        }

        var fromDate = currentDate.AddMonths(-monthsBack);

        return catalogItem.ManufactureHistory
            .Where(m => m.Date >= fromDate)
            .OrderByDescending(m => m.Date)
            .Select(m => new CatalogManufactureRecordDto
            {
                Date = m.Date,
                Amount = m.Amount,
                PricePerPiece = m.PricePerPiece,
                PriceTotal = m.PriceTotal,
                ProductCode = m.ProductCode,
                DocumentNumber = m.DocumentNumber
            }).ToList();
    }

    private List<ManufactureCostDto> GetManufactureCostHistoryFromAggregate(CatalogAggregate catalogItem, int monthsBack)
    {
        // Return individual manufacture cost records
        var currentDate = _timeProvider.GetUtcNow().Date;

        // For very high monthsBack values (like ALL_HISTORY_MONTHS_THRESHOLD), return all records without date filtering
        if (monthsBack >= CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
        {
            return catalogItem.ManufactureCostHistory
                .OrderByDescending(mc => mc.Date)
                .Select(mc => new ManufactureCostDto
                {
                    Date = mc.Date,
                    MaterialCost = mc.MaterialCost,
                    HandlingCost = mc.HandlingCost,
                    Total = mc.Total
                }).ToList();
        }

        var fromDate = currentDate.AddMonths(-monthsBack);

        return catalogItem.ManufactureCostHistory
            .Where(mc => mc.Date >= fromDate)
            .OrderByDescending(mc => mc.Date)
            .Select(mc => new ManufactureCostDto
            {
                Date = mc.Date,
                MaterialCost = mc.MaterialCost,
                HandlingCost = mc.HandlingCost,
                Total = mc.Total
            }).ToList();
    }

    private List<MarginHistoryDto> GetMarginHistoryFromAggregate(CatalogAggregate catalogItem, int monthsBack)
    {
        // Calculate margin for each month based on manufacturing cost history
        var currentDate = _timeProvider.GetUtcNow().Date;

        // Get selling price without VAT from eshop
        var sellingPrice = catalogItem.EshopPrice?.PriceWithoutVat ?? 0;

        // If no selling price available, return empty list
        if (sellingPrice == 0)
        {
            return new List<MarginHistoryDto>();
        }

        // Filter manufacturing cost history based on monthsBack
        var manufactureCostHistory = catalogItem.ManufactureCostHistory.AsQueryable();

        if (monthsBack < CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
        {
            var fromDate = currentDate.AddMonths(-monthsBack);
            manufactureCostHistory = manufactureCostHistory.Where(mc => mc.Date >= fromDate);
        }

        return manufactureCostHistory
            .OrderByDescending(mc => mc.Date)
            .Select(mc => new MarginHistoryDto
            {
                Date = mc.Date,
                SellingPrice = sellingPrice,
                TotalCost = mc.Total,
                MarginAmount = sellingPrice - mc.Total,
                MarginPercentage = sellingPrice > 0 ? ((sellingPrice - mc.Total) / sellingPrice) * 100 : 0
            }).ToList();
    }
}