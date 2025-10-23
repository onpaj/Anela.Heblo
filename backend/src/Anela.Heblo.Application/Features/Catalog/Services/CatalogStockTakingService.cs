using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Xcc.Services;
using AutoMapper;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public class CatalogStockTakingService : ICatalogStockTakingService
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<CatalogStockTakingService> _logger;
    private readonly IEshopStockDomainService _eshopStockDomainService;
    private readonly IBackgroundWorker _backgroundWorker;
    private readonly IMapper _mapper;

    public CatalogStockTakingService(
        ICatalogRepository catalogRepository,
        ILogger<CatalogStockTakingService> logger,
        IEshopStockDomainService eshopStockDomainService,
        IBackgroundWorker backgroundWorker,
        IMapper mapper)
    {
        _catalogRepository = catalogRepository;
        _logger = logger;
        _eshopStockDomainService = eshopStockDomainService;
        _backgroundWorker = backgroundWorker;
        _mapper = mapper;
    }

    public async Task<string> EnqueueStockTakingAsync(
        string productCode,
        decimal targetAmount,
        bool softStockTaking = false,
        CancellationToken cancellationToken = default)
    {
        // Optimistic update: Update eshop stock cache immediately when job is enqueued
        // This provides immediate feedback to users before the actual stock taking completes
        if (!softStockTaking)
        {
            _logger.LogInformation("Performing optimistic eshop stock cache update for product {ProductCode} to {TargetAmount}", 
                productCode, targetAmount);
            
            try
            {
                // Update product's EshopStock directly
                var product = await _catalogRepository.GetByIdAsync(productCode);
                if (product?.Stock != null)
                {
                    product.Stock.Eshop = targetAmount;
                    _logger.LogInformation("Successfully updated product eshop stock for {ProductCode} to {TargetAmount} (optimistic)", 
                        productCode, targetAmount);
                }
                else
                {
                    _logger.LogWarning("Product {ProductCode} or its eshop stock not found for optimistic update", productCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to perform optimistic eshop stock update for product {ProductCode}", productCode);
                // Don't fail the enqueue operation if optimistic update fails
            }
        }

        var jobId = _backgroundWorker.Enqueue<ICatalogStockTakingService>(
            service => service.ProcessStockTakingAsync(productCode, targetAmount, softStockTaking, cancellationToken));

        return jobId;
    }

    
    [DisplayName("Inventarizace-{0}-{1}")]
    public async Task<StockTakingResultDto> ProcessStockTakingAsync(
        string productCode,
        decimal targetAmount,
        bool softStockTaking = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing stock taking for product code {ProductCode} with target amount {TargetAmount}",
            productCode, targetAmount);

        try
        {
            // Map request to domain model
            var eshopRequest = new EshopStockTakingRequest
            {
                ProductCode = productCode,
                TargetAmount = targetAmount,
                SoftStockTaking = softStockTaking
            };

            // Call domain service to submit stock taking
            var stockTakingRecord = await _eshopStockDomainService.SubmitStockTakingAsync(eshopRequest);

            // Check if the stock taking operation failed
            if (!string.IsNullOrEmpty(stockTakingRecord.Error))
            {
                _logger.LogWarning("Stock taking failed for product code {ProductCode}. Error: {Error}",
                    productCode, stockTakingRecord.Error);

                return new StockTakingResultDto
                {
                    Id = stockTakingRecord.Id.ToString(),
                    Type = stockTakingRecord.Type.ToString(),
                    Code = stockTakingRecord.Code,
                    AmountNew = stockTakingRecord.AmountNew,
                    AmountOld = stockTakingRecord.AmountOld,
                    Date = stockTakingRecord.Date,
                    User = stockTakingRecord.User ?? string.Empty,
                    Error = stockTakingRecord.Error ?? string.Empty
                };
            }

            _logger.LogInformation("Stock taking processed successfully for product code {ProductCode}. Record ID: {RecordId}",
                productCode, stockTakingRecord.Id);

            var product = await _catalogRepository.GetByIdAsync(productCode, cancellationToken);
            if (product != null)
            {
                // Use the new SyncStockTaking method to update stock and add to history
                product.SyncStockTaking(stockTakingRecord);
                // Note: Catalog repository is read-only, changes will be persisted through other mechanisms
            }

            // Map domain result to response
            return _mapper.Map<StockTakingResultDto>(stockTakingRecord);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process stock taking for product code {ProductCode}", productCode);
            return new StockTakingResultDto
            {
                Code = productCode,
                Error = ex.Message,
                Date = DateTime.UtcNow
            };
        }
    }
}