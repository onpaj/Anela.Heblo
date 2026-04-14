using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public class EshopStockDomainService : IEshopStockDomainService
{
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly IEshopStockClient _stockClient;
    private readonly ILogger<EshopStockDomainService> _logger;

    public EshopStockDomainService(
        IStockTakingRepository stockTakingRepository,
        ICurrentUserService currentUser,
        TimeProvider timeProvider,
        IEshopStockClient stockClient,
        ILogger<EshopStockDomainService> logger)
    {
        _stockTakingRepository = stockTakingRepository;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _stockClient = stockClient;
        _logger = logger;
    }

    public async Task StockUpAsync(StockUpRequest stockUpOrder)
    {
        await _stockClient.UpdateStockAsync(stockUpOrder.ProductCode, stockUpOrder.Amount);
    }

    /// <summary>
    /// The Shoptet REST API does not support searching movements by document number,
    /// so this check is not possible. Returns false to allow the caller to proceed with submit.
    /// Traceability is maintained in Heblo's StockUpOperation table via DocumentNumber.
    /// </summary>
    public Task<bool> VerifyStockUpExistsAsync(string documentNumber)
        => Task.FromResult(false);

    /// <summary>
    /// Submits a stock taking operation to Shoptet via the REST API.
    /// DryRun is not supported — stock changes are always applied.
    /// </summary>
    public async Task<StockTakingRecord> SubmitStockTakingAsync(EshopStockTakingRequest order)
    {
        try
        {
            StockTakingRecord result;
            if (!order.SoftStockTaking)
            {
                var supply = await _stockClient.GetSupplyAsync(order.ProductCode);
                var amountOld = (supply?.Amount ?? 0) + (supply?.Claim ?? 0);

                await _stockClient.SetRealStockAsync(order.ProductCode, (double)order.TargetAmount);

                result = new StockTakingRecord
                {
                    Code = order.ProductCode,
                    AmountNew = (double)order.TargetAmount,
                    AmountOld = amountOld,
                };
            }
            else
            {
                result = new StockTakingRecord
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
            _logger.LogError(e, "Stock taking failed for product {ProductCode}", order.ProductCode);
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
