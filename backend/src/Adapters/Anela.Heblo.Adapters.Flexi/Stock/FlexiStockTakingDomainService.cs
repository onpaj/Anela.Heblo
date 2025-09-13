using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Users;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockTaking;
using Rem.FlexiBeeSDK.Model.Products.StockTaking;

namespace Anela.Heblo.Adapters.Flexi.Stock;


public class FlexiStockTakingDomainService : IErpStockDomainService
{
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly IStockTakingClient _stockTakingClient;
    private readonly IStockTakingItemsClient _stockTakingItemsClient;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _timeProvider;

    private const int MaterialWarehouseId = 5;
    private const string OwnerName = "Heblo";
    private const string MaterialStockTakingType = "Material";
    private const int SubmitDocumentTypeId = 60;

    public FlexiStockTakingDomainService(
        IStockTakingRepository stockTakingRepository,
        IStockTakingClient stockTakingClient,
        IStockTakingItemsClient stockTakingItemsClient,
        ICurrentUserService currentUser,
        TimeProvider timeProvider)
    {
        _stockTakingRepository = stockTakingRepository;
        _stockTakingClient = stockTakingClient;
        _stockTakingItemsClient = stockTakingItemsClient;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }


    public async Task<StockTakingRecord> SubmitStockTakingAsync(ErpStockTakingRequest order)
    {
        try
        {
            StockTakingRecord result;
            if (!order.SoftStockTaking) // No real stock taking, just a record in DB
            {
                var headerRequest = new StockTakingHeaderRequest
                {
                    WarehouseId = MaterialWarehouseId,
                    Date = _timeProvider.GetUtcNow().DateTime,
                    Executer = _currentUser.GetCurrentUser().Name,
                    Owner = OwnerName,
                    Type = $"{MaterialStockTakingType}-{order.ProductCode}",
                };
                var header = await _stockTakingClient.CreateHeaderAsync(headerRequest);

                var newItems = order.StockTakingItems.Select(item => new AddStockTakingItemRequest
                {
                    ProductCode = order.ProductCode,
                    Amount = item.Amount,
                    Lot = item.LotCode ?? "",
                    Expiration = item.Expiration?.ToString("yyyy-MM-dd") ?? "",
                }).ToList();

                await _stockTakingItemsClient.AddStockTakingsAsync(header.Id, MaterialWarehouseId, newItems);

                if (order.RemoveMissingLots)
                {
                    var currentItems = await _stockTakingItemsClient.GetStockTakingsAsync(header.Id);
                    var productIds = currentItems.Select(i => i.ProductId).ToList();
                    await _stockTakingClient.AddMissingLotsAsync(header.Id, productIds);
                }

                var itemsBefore = await _stockTakingItemsClient.GetStockTakingsAsync(header.Id);
                if (!order.DryRun)
                {
                    await _stockTakingClient.SubmitAsync(header.Id, SubmitDocumentTypeId);
                }


                header = await _stockTakingClient.GetHeaderAsync(header.Id);
                var itemsAfter = await _stockTakingItemsClient.GetStockTakingsAsync(header.Id);

                result = new StockTakingRecord()
                {
                    Code = order.ProductCode,
                    AmountNew = itemsAfter.Sum(s => s.AmountFound),
                    AmountOld = itemsBefore.Sum(s => s.AmountErp),
                };
            }
            else
            {
                result = new StockTakingRecord()
                {
                    Code = order.ProductCode,
                    AmountNew = (double)order.StockTakingItems.Sum(s => s.Amount), // TODO COnvert to decimal
                    AmountOld = (double)order.StockTakingItems.Sum(s => s.Amount), // TODO COnvert to decimal
                };
            }

            result.User = _currentUser.GetCurrentUser().Name;
            result.Date = _timeProvider.GetUtcNow().DateTime;
            result.Type = StockTakingType.Erp;
            await _stockTakingRepository.AddAsync(result);
            await _stockTakingRepository.SaveChangesAsync();
            return result;
        }
        catch (Exception e)
        {
            return new StockTakingRecord
            {
                Date = _timeProvider.GetUtcNow().DateTime,
                Code = order.ProductCode,
                AmountNew = (double)order.StockTakingItems.Sum(s => s.Amount), // TODO COnvert to decimal
                Error = e.Message
            };
        }
    }
}