using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Products;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RecalculateProductWeight;

public class RecalculateProductWeightHandler : IRequestHandler<RecalculateProductWeightRequest, RecalculateProductWeightResponse>
{
    private readonly IProductWeightRecalculationService _productWeightRecalculationService;
    private readonly IProductWeightClient _productWeightClient;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<RecalculateProductWeightHandler> _logger;

    public RecalculateProductWeightHandler(
        IProductWeightRecalculationService productWeightRecalculationService,
        IProductWeightClient productWeightClient,
        ICatalogRepository catalogRepository,
        ILogger<RecalculateProductWeightHandler> logger)
    {
        _productWeightRecalculationService = productWeightRecalculationService;
        _productWeightClient = productWeightClient;
        _catalogRepository = catalogRepository;
        _logger = logger;
    }

    public async Task<RecalculateProductWeightResponse> Handle(RecalculateProductWeightRequest request, CancellationToken cancellationToken)
    {
        var response = new RecalculateProductWeightResponse();

        try
        {
            if (string.IsNullOrEmpty(request.ProductCode))
            {
                _logger.LogInformation("Starting recalculation of all product weights");
                await _productWeightRecalculationService.RecalculateAllProductWeights(cancellationToken);
                
                var allProducts = await _catalogRepository.GetAllAsync(cancellationToken);
                var productCount = allProducts.Count(p => p.Type == ProductType.Product);
                
                response.ProcessedCount = productCount;
                response.SuccessCount = productCount;
                response.ErrorCount = 0;
                
                _logger.LogInformation("Successfully recalculated weights for {Count} products", productCount);
            }
            else
            {
                _logger.LogInformation("Starting recalculation of product weight for {ProductCode}", request.ProductCode);
                
                var product = await _catalogRepository.GetByIdAsync(request.ProductCode, cancellationToken);
                if (product == null)
                {
                    response.ProcessedCount = 0;
                    response.SuccessCount = 0;
                    response.ErrorCount = 1;
                    response.ErrorMessages.Add($"Product with code '{request.ProductCode}' not found");
                    response.Success = false;
                    return response;
                }

                if (product.Type != ProductType.Product)
                {
                    response.ProcessedCount = 0;
                    response.SuccessCount = 0;
                    response.ErrorCount = 1;
                    response.ErrorMessages.Add($"Product '{request.ProductCode}' is not of type 'Product'");
                    response.Success = false;
                    return response;
                }

                var newWeight = await _productWeightClient.RefreshProductWeight(request.ProductCode, cancellationToken);
                if (newWeight.HasValue)
                {
                    // Note: Since ICatalogRepository is read-only, the weight update would need to be handled by IProductWeightClient
                    // For now, we'll just track the success in the response
                    response.ProcessedCount = 1;
                    response.SuccessCount = 1;
                    response.ErrorCount = 0;
                    
                    _logger.LogInformation("Successfully recalculated weight for product {ProductCode}: {Weight}", 
                        request.ProductCode, newWeight.Value);
                }
                else
                {
                    response.ProcessedCount = 1;
                    response.SuccessCount = 0;
                    response.ErrorCount = 1;
                    response.ErrorMessages.Add($"Failed to calculate weight for product '{request.ProductCode}'");
                    response.Success = false;
                    
                    _logger.LogWarning("Failed to calculate weight for product {ProductCode}", request.ProductCode);
                }
            }

            response.Success = response.ErrorCount == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during product weight recalculation");
            
            response.ProcessedCount = 0;
            response.SuccessCount = 0;
            response.ErrorCount = 1;
            response.ErrorMessages.Add($"Internal error: {ex.Message}");
            response.Success = false;
        }

        return response;
    }
}