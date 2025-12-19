using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;

public class GetCatalogDetailHandler : IRequestHandler<GetCatalogDetailRequest, GetCatalogDetailResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILotsClient _lotsClient;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetCatalogDetailHandler> _logger;

    public GetCatalogDetailHandler(
        ICatalogRepository catalogRepository,
        ILotsClient lotsClient,
        IMapper mapper,
        TimeProvider timeProvider,
        ILogger<GetCatalogDetailHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _lotsClient = lotsClient;
        _mapper = mapper;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GetCatalogDetailResponse> Handle(GetCatalogDetailRequest request, CancellationToken cancellationToken)
    {
        // Get catalog item with all historical data already loaded from cache
        var catalogItem = await _catalogRepository.SingleOrDefaultAsync(
            x => x.ProductCode == request.ProductCode,
            cancellationToken);

        if (catalogItem == null)
        {
            return new GetCatalogDetailResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ProductNotFound,
                Params = new Dictionary<string, string> { { "productCode", request.ProductCode } }
            };
        }

        // Convert historical data to DTOs with monthly grouping
        var salesHistory = GetSalesHistoryFromAggregate(catalogItem, request.MonthsBack);
        var purchaseHistory = GetPurchaseHistoryFromAggregate(catalogItem, request.MonthsBack);
        var consumedHistory = GetConsumedHistoryFromAggregate(catalogItem, request.MonthsBack);
        var manufactureHistory = GetManufactureHistoryFromAggregate(catalogItem, request.MonthsBack);
        var manufactureCostHistory = GetManufactureCostHistoryFromMargins(catalogItem, request.MonthsBack);
        var marginHistory = GetMarginHistoryFromMargins(catalogItem, request.MonthsBack);

        // Get catalog item DTO
        var catalogItemDto = _mapper.Map<CatalogItemDto>(catalogItem);

        // Fetch lots if the product has lots
        if (catalogItem.HasLots)
        {
            catalogItemDto.Lots = catalogItem.Stock.Lots.Select(lot => new LotDto
            {
                LotCode = lot.Lot,
                Amount = lot.Amount,
                Expiration = lot.Expiration
            }).ToList();
        }

        return new GetCatalogDetailResponse
        {
            Item = catalogItemDto,
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

    private List<ManufactureCostDto> GetManufactureCostHistoryFromMargins(CatalogAggregate catalogItem, int monthsBack)
    {
        try
        {
            var currentDate = _timeProvider.GetUtcNow().Date;

            // Calculate date range
            DateTime fromDate;

            if (monthsBack >= CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
            {
                // For "all history", start from a very early date
                fromDate = new DateTime(2020, 1, 1);
            }
            else
            {
                fromDate = currentDate.AddMonths(-monthsBack);
            }

            // Use pre-calculated margin data from CatalogAggregate.Margins
            var marginHistory = catalogItem.Margins;

            // Filter and convert margin history to ManufactureCostDto format
            return marginHistory.MonthlyData
                .Where(m => m.Month >= fromDate)
                .OrderByDescending(m => m.Month)
                .Select(m => new ManufactureCostDto
                {
                    Date = m.Month,
                    MaterialCost = m.CostsForMonth.M0CostLevel,
                    HandlingCost = m.CostsForMonth.M1CostLevel, // Map ManufacturingCost to HandlingCost
                    Total = m.CostsForMonth.M0CostLevel + m.CostsForMonth.M1CostLevel
                }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting manufacture cost history for product {ProductCode}", catalogItem.ProductCode);
            return new List<ManufactureCostDto>();
        }
    }

    private List<MarginHistoryDto> GetMarginHistoryFromMargins(CatalogAggregate catalogItem, int monthsBack)
    {
        var currentDate = _timeProvider.GetUtcNow().Date;

        // Calculate date range
        DateTime fromDate;

        if (monthsBack >= CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
        {
            // For "all history", start from a very early date
            fromDate = new DateTime(2020, 1, 1);
        }
        else
        {
            fromDate = currentDate.AddMonths(-monthsBack);
        }

        // Use pre-calculated margin data from CatalogAggregate.Margins
        var marginHistory = catalogItem.Margins;

        // Filter and convert to DTOs with all M0-M3 margin levels
        return marginHistory.MonthlyData
            .Where(m => m.Month >= fromDate)
            .OrderByDescending(m => m.Month)
            .Select(m => new MarginHistoryDto
            {
                Date = m.Month,
                SellingPrice = m.M3.CostBase + m.M3.Amount, // Reconstructed selling price from highest level
                TotalCost = m.M0.CostBase, // Base cost (material + manufacturing)

                // M0 - Material + Manufacturing costs
                M0 = new MarginLevelDto
                {
                    Percentage = m.M0.Percentage,
                    Amount = m.M0.Amount,
                    CostLevel = m.M0.CostLevel,
                    CostTotal = m.M0.CostTotal
                },

                // M1_A - M0 + Manufacturing costs (economic baseline)
                M1_A = new MarginLevelDto
                {
                    Percentage = m.M1_A.Percentage,
                    Amount = m.M1_A.Amount,
                    CostLevel = m.M1_A.CostLevel,
                    CostTotal = m.M1_A.CostTotal
                },

                // M1_B - M0 + Manufacturing costs (actual monthly cost, nullable)
                M1_B = m.M1_B != null ? new MarginLevelDto
                {
                    Percentage = m.M1_B.Percentage,
                    Amount = m.M1_B.Amount,
                    CostLevel = m.M1_B.CostLevel,
                    CostTotal = m.M1_B.CostTotal
                } : null,

                // M2 - M1 + Sales costs
                M2 = new MarginLevelDto
                {
                    Percentage = m.M2.Percentage,
                    Amount = m.M2.Amount,
                    CostLevel = m.M2.CostLevel,
                    CostTotal = m.M2.CostTotal
                },

                // M3 - M2 + Overhead costs (final margin)
                M3 = new MarginLevelDto
                {
                    Percentage = m.M3.Percentage,
                    Amount = m.M3.Amount,
                    CostLevel = m.M3.CostLevel,
                    CostTotal = m.M3.CostTotal
                }
            }).ToList();
    }
}