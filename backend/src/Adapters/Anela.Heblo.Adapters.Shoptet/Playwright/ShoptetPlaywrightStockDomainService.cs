using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Adapters.Shoptet.Playwright;

public class ShoptetPlaywrightStockDomainService : IEshopStockDomainService
{
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly IEshopStockClient _stockClient;
    private readonly bool _dryRun;

    public ShoptetPlaywrightStockDomainService(
        IStockTakingRepository stockTakingRepository,
        ICurrentUserService currentUser,
        TimeProvider timeProvider,
        IEshopStockClient stockClient,
        PlaywrightSourceOptions options)
    {
        _stockTakingRepository = stockTakingRepository;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _stockClient = stockClient;
        _dryRun = options.DryRun;
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
                var supply = await _stockClient.GetSupplyAsync(order.ProductCode);
                var amountOld = (supply?.Amount ?? 0) + (supply?.Claim ?? 0);

                if (!_dryRun)
                {
                    await _stockClient.SetRealStockAsync(order.ProductCode, (double)order.TargetAmount);
                }

                result = new StockTakingRecord
                {
                    Code = order.ProductCode,
                    AmountNew = (double)order.TargetAmount,
                    AmountOld = amountOld,
                };
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
