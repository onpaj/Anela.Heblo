using Anela.Heblo.Application.Features.Catalog.UseCases.ProcessStockTaking;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Xcc.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.EnqueueStockTaking;

public class EnqueueStockTakingHandler : IRequestHandler<EnqueueStockTakingRequest, EnqueueStockTakingResponse>
{
    private readonly IBackgroundWorker _backgroundWorker;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<EnqueueStockTakingHandler> _logger;

    public EnqueueStockTakingHandler(
        IBackgroundWorker backgroundWorker,
        ICatalogRepository catalogRepository,
        ILogger<EnqueueStockTakingHandler> logger)
    {
        _backgroundWorker = backgroundWorker;
        _catalogRepository = catalogRepository;
        _logger = logger;
    }

    public async Task<EnqueueStockTakingResponse> Handle(EnqueueStockTakingRequest request, CancellationToken cancellationToken)
    {
        // Optimistic update: Update eshop stock cache immediately when job is enqueued
        // This provides immediate feedback to users before the actual stock taking completes
        if (!request.SoftStockTaking)
        {
            _logger.LogInformation("Performing optimistic eshop stock cache update for product {ProductCode} to {TargetAmount}", 
                request.ProductCode, request.TargetAmount);
            
            try
            {
                // Update product's EshopStock directly
                var product = await _catalogRepository.GetByIdAsync(request.ProductCode);
                if (product?.Stock != null)
                {
                    product.Stock.Eshop = request.TargetAmount;
                    _logger.LogInformation("Successfully updated product eshop stock for {ProductCode} to {TargetAmount} (optimistic)", 
                        request.ProductCode, request.TargetAmount);
                }
                else
                {
                    _logger.LogWarning("Product {ProductCode} or its eshop stock not found for optimistic update", request.ProductCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to perform optimistic eshop stock update for product {ProductCode}", request.ProductCode);
                // Don't fail the enqueue operation if optimistic update fails
            }
        }

        // Enqueue the background job using the new MediatR handler
        var jobId = _backgroundWorker.Enqueue<IMediator>(
            mediator => mediator.Send(new ProcessStockTakingRequest
            {
                ProductCode = request.ProductCode,
                TargetAmount = request.TargetAmount,
                SoftStockTaking = request.SoftStockTaking
            }, cancellationToken));

        return new EnqueueStockTakingResponse
        {
            JobId = jobId,
            Message = $"Stock taking for {request.ProductCode} with target amount {request.TargetAmount} has been queued. Job ID: {jobId}"
        };
    }
}