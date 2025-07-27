using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Anela.Heblo.Catalog.StockTaking;
using Volo.Abp.Timing;
using Volo.Abp.Users;

namespace Anela.Heblo.Adapters.Shoptet.Playwright;

public class ShoptetPlaywrightStockTakingDomainService : IEshopStockTakingDomainService
{
    private readonly StockUpScenario _stockUpScenario;
    private readonly StockTakingScenario _inventoryAlignScenario;
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public ShoptetPlaywrightStockTakingDomainService(
        StockUpScenario stockUpScenario, 
        StockTakingScenario inventoryAlignScenario,
        IStockTakingRepository stockTakingRepository,
        ICurrentUser currentUser,
        IClock clock)
    {
        _stockUpScenario = stockUpScenario;
        _inventoryAlignScenario = inventoryAlignScenario;
        _stockTakingRepository = stockTakingRepository;
        _currentUser = currentUser;
        _clock = clock;
    }
    
    public async Task StockUpAsync(StockUpRequest stockUpOrder)
    {
        var result = await _stockUpScenario.RunAsync(stockUpOrder);
    }


    public async Task<StockTakingResult> SubmitStockTakingAsync(EshopStockTakingRequest order)
    {
        try
        {
            StockTakingResult result;
            if (!order.SoftStockTaking) // No real stock taking, just a record in DB
            {
                result = await _inventoryAlignScenario.RunAsync(order);
            }
            else
            {
                result = new StockTakingResult()
                {
                    Code = order.ProductCode,
                    AmountNew = (double)order.TargetAmount, // TODO COnvert to decimal
                    AmountOld = (double)order.TargetAmount, // TODO COnvert to decimal
                };
            }
            result.User = _currentUser.Name;
            result.Date = _clock.Now;
            await _stockTakingRepository.InsertAsync(result, autoSave: true);
            return result;
        }
        catch (Exception e)
        {
            return new StockTakingResult
            {
                Date = DateTime.Now,
                Code = order.ProductCode,
                AmountNew = (double)order.TargetAmount, // TODO Convert to decimal
                AmountOld = (double)order.TargetAmount, // TODO Convert to decimal
                Error = e.Message
            };
        }
    }
}