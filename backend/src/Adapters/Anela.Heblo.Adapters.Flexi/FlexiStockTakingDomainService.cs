using Anela.Heblo.Catalog.StockTaking;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockTaking;
using Rem.FlexiBeeSDK.Model.Products.StockTaking;
using Volo.Abp.Timing;
using Volo.Abp.Users;

namespace Anela.Heblo.Adapters.Flexi;


public class FlexiStockTakingDomainService : IErpStockTakingDomainService
{
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly IStockTakingClient _stockTakingClient;
    private readonly IStockTakingItemsClient _stockTakingItemsClient;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    private const int MaterialWarehouseId = 5;
    private const string OwnerName = "Heblo";
    private const string MaterialStockTakingType = "Material";
    private const int SubmitDocumentTypeId = 60;

    public FlexiStockTakingDomainService(
        IStockTakingRepository stockTakingRepository,
        IStockTakingClient stockTakingClient,
        IStockTakingItemsClient stockTakingItemsClient,
        ICurrentUser currentUser,
        IClock clock)
    {
        _stockTakingRepository = stockTakingRepository;
        _stockTakingClient = stockTakingClient;
        _stockTakingItemsClient = stockTakingItemsClient;
        _currentUser = currentUser;
        _clock = clock;
    }
    

    public async Task<StockTakingResult> SubmitStockTakingAsync(ErpStockTakingRequest order)
    {
        try
        {
            StockTakingResult result;
            if (!order.SoftStockTaking) // No real stock taking, just a record in DB
            {
                var headerRequest = new StockTakingHeaderRequest
                {
                    WarehouseId = MaterialWarehouseId,
                    Date = _clock.Now,
                    Executer = _currentUser.Name,
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
                
                result = new StockTakingResult()
                {
                    Code = order.ProductCode,
                    AmountNew = itemsAfter.Sum(s => s.AmountFound),
                    AmountOld = itemsBefore.Sum(s => s.AmountErp),
                };
            }
            else
            {
                result = new StockTakingResult()
                {
                    Code = order.ProductCode,
                    AmountNew = (double)order.StockTakingItems.Sum(s => s.Amount), // TODO COnvert to decimal
                    AmountOld = (double)order.StockTakingItems.Sum(s => s.Amount), // TODO COnvert to decimal
                };
            }
            result.User = _currentUser.Name;
            result.Date = _clock.Now;
            result.Type = StockTakingType.Erp;
            await _stockTakingRepository.InsertAsync(result, autoSave: true);
            return result;
        }
        catch (Exception e)
        {
            return new StockTakingResult
            {
                Date = DateTime.Now,
                Code = order.ProductCode,
                AmountNew = (double)order.StockTakingItems.Sum(s => s.Amount), // TODO COnvert to decimal
                Error = e.Message
            };
        }
    }
}