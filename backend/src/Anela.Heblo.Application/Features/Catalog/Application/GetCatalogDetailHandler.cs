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
    private readonly ICatalogSalesClient _salesClient;
    private readonly IPurchaseHistoryClient _purchaseHistoryClient;
    private readonly IConsumedMaterialsClient _consumedMaterialsClient;
    private readonly IMapper _mapper;

    public GetCatalogDetailHandler(
        ICatalogRepository catalogRepository,
        ICatalogSalesClient salesClient,
        IPurchaseHistoryClient purchaseHistoryClient,
        IConsumedMaterialsClient consumedMaterialsClient,
        IMapper mapper)
    {
        _catalogRepository = catalogRepository;
        _salesClient = salesClient;
        _purchaseHistoryClient = purchaseHistoryClient;
        _consumedMaterialsClient = consumedMaterialsClient;
        _mapper = mapper;
    }

    public async Task<GetCatalogDetailResponse> Handle(GetCatalogDetailRequest request, CancellationToken cancellationToken)
    {
        // Get basic catalog item by exact product code match
        var catalogItems = await _catalogRepository.FindAsync(
            x => x.ProductCode == request.ProductCode, 
            cancellationToken);

        var catalogItem = catalogItems.FirstOrDefault();
        if (catalogItem == null)
        {
            throw new InvalidOperationException($"Product with code '{request.ProductCode}' not found.");
        }

        // Get historical data
        var salesHistory = await GetSalesHistoryAsync(request.ProductCode, cancellationToken);
        var purchaseHistory = await GetPurchaseHistoryAsync(request.ProductCode, cancellationToken);
        var consumedHistory = await GetConsumedHistoryAsync(request.ProductCode, cancellationToken);

        return new GetCatalogDetailResponse
        {
            Item = _mapper.Map<CatalogItemDto>(catalogItem),
            HistoricalData = new CatalogHistoricalDataDto
            {
                SalesHistory = salesHistory.OrderByDescending(x => x.Date).ToList(),
                PurchaseHistory = purchaseHistory.OrderByDescending(x => x.Date).ToList(),
                ConsumedHistory = consumedHistory.OrderByDescending(x => x.Date).ToList()
            }
        };
    }

    private async Task<List<CatalogSalesRecordDto>> GetSalesHistoryAsync(string productCode, CancellationToken cancellationToken)
    {
        try
        {
            // Get sales data for last 30 days and filter by product code
            var fromDate = DateTime.UtcNow.Date.AddDays(-30);
            var toDate = DateTime.UtcNow.Date;
            var salesData = await _salesClient.GetAsync(fromDate, toDate, 0, cancellationToken);
            
            return salesData
                .Where(s => s.ProductCode == productCode)
                .Select(s => new CatalogSalesRecordDto
                {
                    Date = s.Date,
                    AmountTotal = s.AmountTotal,
                    AmountB2B = s.AmountB2B,
                    AmountB2C = s.AmountB2C,
                    SumTotal = s.SumTotal,
                    SumB2B = s.SumB2B,
                    SumB2C = s.SumB2C
                }).ToList();
        }
        catch
        {
            // Return empty list if sales data not available
            return new List<CatalogSalesRecordDto>();
        }
    }

    private async Task<List<CatalogPurchaseRecordDto>> GetPurchaseHistoryAsync(string productCode, CancellationToken cancellationToken)
    {
        try
        {
            // Get purchase history for last 30 days for specific product
            var fromDate = DateTime.UtcNow.Date.AddDays(-30);
            var toDate = DateTime.UtcNow.Date;
            var purchaseData = await _purchaseHistoryClient.GetHistoryAsync(productCode, fromDate, toDate, 0, cancellationToken);
            
            return purchaseData.Select(p => new CatalogPurchaseRecordDto
            {
                Date = p.Date,
                SupplierName = p.SupplierName ?? string.Empty,
                Amount = p.Amount,
                PricePerPiece = p.PricePerPiece,
                PriceTotal = p.PriceTotal,
                DocumentNumber = p.DocumentNumber ?? string.Empty
            }).ToList();
        }
        catch
        {
            // Return empty list if purchase data not available
            return new List<CatalogPurchaseRecordDto>();
        }
    }

    private async Task<List<CatalogConsumedRecordDto>> GetConsumedHistoryAsync(string productCode, CancellationToken cancellationToken)
    {
        try
        {
            // Get consumed materials for last 30 days and filter by product code
            var fromDate = DateTime.UtcNow.Date.AddDays(-30);
            var toDate = DateTime.UtcNow.Date;
            var consumedData = await _consumedMaterialsClient.GetConsumedAsync(fromDate, toDate, 0, cancellationToken);
            
            return consumedData
                .Where(c => c.ProductCode == productCode)
                .Select(c => new CatalogConsumedRecordDto
                {
                    Date = c.Date,
                    Amount = c.Amount,
                    ProductName = c.ProductName ?? string.Empty
                }).ToList();
        }
        catch
        {
            // Return empty list if consumed data not available
            return new List<CatalogConsumedRecordDto>();
        }
    }
}