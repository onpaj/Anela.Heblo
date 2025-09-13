using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.SubmitStockTaking;

public class SubmitStockTakingHandler : IRequestHandler<SubmitStockTakingRequest, SubmitStockTakingResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<SubmitStockTakingHandler> _logger;
    private readonly IEshopStockDomainService _eshopStockDomainService;

    public SubmitStockTakingHandler(
        ICatalogRepository catalogRepository,
        ILogger<SubmitStockTakingHandler> logger,
        IEshopStockDomainService eshopStockDomainService)
    {
        _catalogRepository = catalogRepository;
        _logger = logger;
        _eshopStockDomainService = eshopStockDomainService;
    }

    public async Task<SubmitStockTakingResponse> Handle(SubmitStockTakingRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Submitting stock taking for product code {ProductCode} with target amount {TargetAmount}",
            request.ProductCode, request.TargetAmount);

        try
        {
            // Map request to domain model
            var eshopRequest = new EshopStockTakingRequest
            {
                ProductCode = request.ProductCode,
                TargetAmount = request.TargetAmount,
                SoftStockTaking = request.SoftStockTaking
            };

            // Call domain service to submit stock taking
            var stockTakingRecord = await _eshopStockDomainService.SubmitStockTakingAsync(eshopRequest);

            // Check if the stock taking operation failed
            if (!string.IsNullOrEmpty(stockTakingRecord.Error))
            {
                _logger.LogWarning("Stock taking failed for product code {ProductCode}. Error: {Error}",
                    request.ProductCode, stockTakingRecord.Error);

                return new SubmitStockTakingResponse(ErrorCodes.StockTakingFailed,
                    new Dictionary<string, string>
                    {
                        { "ProductCode", request.ProductCode },
                        { "Error", stockTakingRecord.Error }
                    });
            }

            _logger.LogInformation("Stock taking submitted successfully for product code {ProductCode}. Record ID: {RecordId}",
                request.ProductCode, stockTakingRecord.Id);

            var product = await _catalogRepository.GetByIdAsync(request.ProductCode, cancellationToken);
            if (product != null)
            {
                // Use the new SyncStockTaking method to update stock and add to history
                product.SyncStockTaking(stockTakingRecord);
            }

            // Map domain result to response
            return new SubmitStockTakingResponse
            {
                Id = stockTakingRecord.Id,
                Type = stockTakingRecord.Type,
                Code = stockTakingRecord.Code,
                AmountNew = stockTakingRecord.AmountNew,
                AmountOld = stockTakingRecord.AmountOld,
                Date = stockTakingRecord.Date,
                User = stockTakingRecord.User,
                Error = stockTakingRecord.Error
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit stock taking for product code {ProductCode}", request.ProductCode);
            return new SubmitStockTakingResponse(ErrorCodes.InternalServerError,
                new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
        }
    }
}