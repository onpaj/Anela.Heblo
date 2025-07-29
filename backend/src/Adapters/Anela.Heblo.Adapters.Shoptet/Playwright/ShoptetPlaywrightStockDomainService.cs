using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Anela.Heblo.Application.Domain.Catalog.Stock;
using Anela.Heblo.Application.Domain.Users;

namespace Anela.Heblo.Adapters.Shoptet.Playwright;

public class ShoptetPlaywrightStockDomainService : IEshopStockDomainService
{
    private readonly StockUpScenario _stockUpScenario;
    private readonly StockTakingScenario _inventoryAlignScenario;
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _timeProvider;

    public ShoptetPlaywrightStockDomainService(
        StockUpScenario stockUpScenario, 
        StockTakingScenario inventoryAlignScenario,
        IStockTakingRepository stockTakingRepository,
        ICurrentUserService currentUser,
        TimeProvider timeProvider)
    {
        _stockUpScenario = stockUpScenario;
        _inventoryAlignScenario = inventoryAlignScenario;
        _stockTakingRepository = stockTakingRepository;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }
    
    public async Task StockUpAsync(StockUpRequest stockUpOrder)
    {
        var result = await _stockUpScenario.RunAsync(stockUpOrder);
    }


    public async Task<StockTakingRecord> SubmitStockTakingAsync(EshopStockTakingRequest order)
    {
        try
        {
            StockTakingRecord result;
            if (!order.SoftStockTaking) // No real stock taking, just a record in DB
            {
                result = await _inventoryAlignScenario.RunAsync(order);
            }
            else
            {
                result = new StockTakingRecord()
                {
                    Code = order.ProductCode,
                    AmountNew = (double)order.TargetAmount, // TODO COnvert to decimal
                    AmountOld = (double)order.TargetAmount, // TODO COnvert to decimal
                };
            }
            result.User = _currentUser.GetCurrentUser().Name;
            result.Date = _timeProvider.GetUtcNow().DateTime;
            await _stockTakingRepository.AddAsync(result);
            return result;
        }
        catch (Exception e)
        {
            return new StockTakingRecord
            {
                Date = _timeProvider.GetUtcNow().DateTime,
                Code = order.ProductCode,
                AmountNew = (double)order.TargetAmount, // TODO Convert to decimal
                AmountOld = (double)order.TargetAmount, // TODO Convert to decimal
                Error = e.Message
            };
        }
    }
}