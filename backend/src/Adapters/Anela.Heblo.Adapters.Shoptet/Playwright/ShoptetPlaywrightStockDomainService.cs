using Anela.Heblo.Application.Features.Catalog.Stock;
using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Adapters.Shoptet.Playwright;

public class ShoptetPlaywrightStockDomainService : IEshopStockDomainService
{
    private readonly IShoptetStockClient _stockClient;
    private readonly StockTakingScenario _inventoryAlignScenario;
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _timeProvider;

    public ShoptetPlaywrightStockDomainService(
        IShoptetStockClient stockClient,
        StockTakingScenario inventoryAlignScenario,
        IStockTakingRepository stockTakingRepository,
        ICurrentUserService currentUser,
        TimeProvider timeProvider)
    {
        _stockClient = stockClient;
        _inventoryAlignScenario = inventoryAlignScenario;
        _stockTakingRepository = stockTakingRepository;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public async Task StockUpAsync(StockUpRequest stockUpOrder)
    {
        foreach (var product in stockUpOrder.Products)
        {
            await _stockClient.UpdateStockAsync(product.ProductCode, product.Amount);
        }
    }

    public Task<bool> VerifyStockUpExistsAsync(string documentNumber)
    {
        // REST API has no document-number search. Pre-check evaluates to "not found, proceed".
        return Task.FromResult(false);
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
            await _stockTakingRepository.SaveChangesAsync();
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
