using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Anela.Heblo.Application.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Adapters.Shoptet.Playwright;

public class ShoptetPlaywrightStockDomainService : IEshopStockDomainService
{
    private readonly StockTakingScenario _inventoryAlignScenario;
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly IShoptetStockClient _stockClient;

    public ShoptetPlaywrightStockDomainService(
        StockTakingScenario inventoryAlignScenario,
        IStockTakingRepository stockTakingRepository,
        ICurrentUserService currentUser,
        TimeProvider timeProvider,
        IShoptetStockClient stockClient)
    {
        _inventoryAlignScenario = inventoryAlignScenario;
        _stockTakingRepository = stockTakingRepository;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _stockClient = stockClient;
    }

    public async Task StockUpAsync(StockUpRequest stockUpOrder)
    {
        foreach (var product in stockUpOrder.Products)
        {
            await _stockClient.UpdateStockAsync(product.ProductCode, product.Amount);
        }
    }

    /// <summary>
    /// The Shoptet REST API does not support searching movements by document number,
    /// so this check is not possible. Returns false to allow the caller to proceed with submit.
    /// Traceability is maintained in Heblo's StockUpOperation table via DocumentNumber.
    /// </summary>
    public Task<bool> VerifyStockUpExistsAsync(string documentNumber)
        => Task.FromResult(false);

    public async Task<StockTakingRecord> SubmitStockTakingAsync(EshopStockTakingRequest order)
    {
        try
        {
            StockTakingRecord result;
            if (!order.SoftStockTaking)
            {
                result = await _inventoryAlignScenario.RunAsync(order);
            }
            else
            {
                result = new StockTakingRecord()
                {
                    Code = order.ProductCode,
                    AmountNew = (double)order.TargetAmount,
                    AmountOld = (double)order.TargetAmount,
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
                AmountNew = (double)order.TargetAmount,
                AmountOld = (double)order.TargetAmount,
                Error = e.Message,
            };
        }
    }
}
