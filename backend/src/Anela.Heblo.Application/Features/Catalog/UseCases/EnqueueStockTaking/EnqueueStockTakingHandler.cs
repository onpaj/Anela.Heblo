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
    private readonly IMediator _mediator;

    public EnqueueStockTakingHandler(
        IBackgroundWorker backgroundWorker,
        ICatalogRepository catalogRepository,
        ILogger<EnqueueStockTakingHandler> logger,
        IMediator mediator)
    {
        _backgroundWorker = backgroundWorker;
        _catalogRepository = catalogRepository;
        _logger = logger;
        _mediator = mediator;
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

        // Enqueue the background job using the new MediatR handler with error checking
        var jobId = _backgroundWorker.Enqueue<EnqueueStockTakingHandler>(
            handler => handler.ExecuteStockTakingJobAsync(request.ProductCode, request.TargetAmount, request.SoftStockTaking));

        return new EnqueueStockTakingResponse
        {
            JobId = jobId,
            Message = $"Stock taking for {request.ProductCode} with target amount {request.TargetAmount} has been queued. Job ID: {jobId}"
        };
    }

    /// <summary>
    /// Background job method that executes stock taking and throws exception on error for Hangfire retry
    /// </summary>
    public async Task ExecuteStockTakingJobAsync(string productCode, decimal targetAmount, bool softStockTaking)
    {
        var response = await _mediator.Send(new ProcessStockTakingRequest
        {
            ProductCode = productCode,
            TargetAmount = targetAmount,
            SoftStockTaking = softStockTaking
        }, CancellationToken.None);
        
        // Check if the response contains an error
        if (!string.IsNullOrEmpty(response.Result?.Error))
        {
            throw new InvalidOperationException($"Stock taking failed: {response.Result.Error}");
        }
        
        // Also check the Success flag from BaseResponse
        if (!response.Success)
        {
            throw new InvalidOperationException($"Stock taking failed with error code: {response.ErrorCode}");
        }
    }
}