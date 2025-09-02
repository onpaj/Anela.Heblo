using Anela.Heblo.Application.Features.Purchase.Requests;
using Anela.Heblo.Application.Features.Purchase.Responses;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.Handlers;

public class RecalculatePurchasePriceHandler : IRequestHandler<RecalculatePurchasePriceRequest, RecalculatePurchasePriceResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IProductPriceErpClient _productPriceClient;
    private readonly ILogger<RecalculatePurchasePriceHandler> _logger;

    public RecalculatePurchasePriceHandler(
        ICatalogRepository catalogRepository,
        IProductPriceErpClient productPriceClient,
        ILogger<RecalculatePurchasePriceHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _productPriceClient = productPriceClient;
        _logger = logger;
    }

    public async Task<RecalculatePurchasePriceResponse> Handle(RecalculatePurchasePriceRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting price recalculation - ProductCode: {ProductCode}, RecalculateAll: {RecalculateAll}", 
            request.ProductCode, request.RecalculateAll);

        // Validate request
        if (string.IsNullOrEmpty(request.ProductCode) && !request.RecalculateAll)
        {
            throw new ArgumentException("Either ProductCode must be specified or RecalculateAll must be true");
        }

        var products = await _catalogRepository.GetAllAsync(cancellationToken);
        var response = new RecalculatePurchasePriceResponse();

        List<CatalogAggregate> productsToProcess;

        if (!string.IsNullOrEmpty(request.ProductCode))
        {
            // Single product recalculation
            var product = products.SingleOrDefault(p => p.ProductCode == request.ProductCode);
            if (product == null)
            {
                throw new ArgumentException($"Product with code '{request.ProductCode}' not found");
            }

            productsToProcess = new List<CatalogAggregate> { product };
            _logger.LogInformation("Processing single product: {ProductCode}", request.ProductCode);
        }
        else
        {
            // All products with BoM recalculation
            productsToProcess = products.Where(p => p.HasBoM).ToList();
            _logger.LogInformation("Processing {Count} products with BoM", productsToProcess.Count);
        }

        response.TotalCount = productsToProcess.Count;

        // Process each product
        foreach (var product in productsToProcess)
        {
            try
            {
                await RecalculateSingleProduct(product, cancellationToken);
                
                response.ProcessedProducts.Add(new ProductRecalculationResult
                {
                    ProductCode = product.ProductCode,
                    IsSuccess = true,
                    ErrorMessage = null
                });
                
                response.SuccessCount++;
                _logger.LogDebug("Successfully recalculated price for product {ProductCode}", product.ProductCode);
            }
            catch (Exception ex)
            {
                response.FailedCount++;
                var errorMessage = ex.Message;
                
                response.ProcessedProducts.Add(new ProductRecalculationResult
                {
                    ProductCode = product.ProductCode,
                    IsSuccess = false,
                    ErrorMessage = errorMessage
                });
                
                _logger.LogError(ex, "Failed to recalculate price for product {ProductCode}: {ErrorMessage}", 
                    product.ProductCode, errorMessage);
            }
        }

        _logger.LogInformation("Price recalculation completed - Success: {SuccessCount}, Failed: {FailedCount}, Total: {TotalCount}", 
            response.SuccessCount, response.FailedCount, response.TotalCount);

        return response;
    }


    private async Task RecalculateSingleProduct(CatalogAggregate product, CancellationToken cancellationToken)
    {
        if (!product.BoMId.HasValue)
        {
            throw new InvalidOperationException($"Product {product.ProductCode} does not have BoM");
        }

        _logger.LogDebug("Recalculating price for product {ProductCode} with BoMId {BoMId}", 
            product.ProductCode, product.BoMId.Value);

        await _productPriceClient.RecalculatePurchasePrice(product.BoMId.Value, cancellationToken);
    }
}