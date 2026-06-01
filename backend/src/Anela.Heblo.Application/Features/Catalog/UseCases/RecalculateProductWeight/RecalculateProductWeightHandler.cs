using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RecalculateProductWeight;

public class RecalculateProductWeightHandler : IRequestHandler<RecalculateProductWeightRequest, RecalculateProductWeightResponse>
{
    private readonly IProductWeightRecalculationService _productWeightRecalculationService;
    private readonly ILogger<RecalculateProductWeightHandler> _logger;

    public RecalculateProductWeightHandler(
        IProductWeightRecalculationService productWeightRecalculationService,
        ILogger<RecalculateProductWeightHandler> logger)
    {
        _productWeightRecalculationService = productWeightRecalculationService;
        _logger = logger;
    }

    public async Task<RecalculateProductWeightResponse> Handle(RecalculateProductWeightRequest request, CancellationToken cancellationToken)
    {
        try
        {
            ProductWeightRecalculationResult result;

            if (string.IsNullOrEmpty(request.ProductCode))
            {
                _logger.LogInformation("Starting recalculation of all product weights via MediatR handler");
                result = await _productWeightRecalculationService.RecalculateAllProductWeights(cancellationToken);
            }
            else
            {
                _logger.LogInformation("Starting recalculation of product weight for {ProductCode} via MediatR handler", request.ProductCode);
                result = await _productWeightRecalculationService.RecalculateProductWeight(request.ProductCode, cancellationToken);
            }

            // Map service result to response
            var response = new RecalculateProductWeightResponse
            {
                ProcessedCount = result.ProcessedCount,
                SuccessCount = result.SuccessCount,
                ErrorCount = result.ErrorCount,
                ErrorMessages = result.ErrorMessages,
                Success = result.ErrorCount == 0
            };

            _logger.LogInformation("Product weight recalculation completed via MediatR. Success: {SuccessCount}, Errors: {ErrorCount}, Duration: {Duration}",
                result.SuccessCount, result.ErrorCount, result.Duration);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during product weight recalculation in MediatR handler");

            return new RecalculateProductWeightResponse
            {
                ProcessedCount = 0,
                SuccessCount = 0,
                ErrorCount = 1,
                ErrorMessages = new List<string> { $"Internal error: {ex.Message}" },
                Success = false
            };
        }
    }
}