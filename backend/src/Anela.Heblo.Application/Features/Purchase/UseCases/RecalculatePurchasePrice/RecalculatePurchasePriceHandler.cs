using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Price;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;

public class RecalculatePurchasePriceHandler : IRequestHandler<RecalculatePurchasePriceRequest, RecalculatePurchasePriceResponse>
{
    private readonly IMaterialCatalogService _materialCatalog;
    private readonly IProductPriceErpClient _productPriceClient;
    private readonly ILogger<RecalculatePurchasePriceHandler> _logger;

    public RecalculatePurchasePriceHandler(
        IMaterialCatalogService materialCatalog,
        IProductPriceErpClient productPriceClient,
        ILogger<RecalculatePurchasePriceHandler> logger)
    {
        _materialCatalog = materialCatalog;
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
            _logger.LogWarning("Invalid request: Either ProductCode must be specified or RecalculateAll must be true");
            return new RecalculatePurchasePriceResponse(ErrorCodes.InvalidValue, new Dictionary<string, string> { { "Message", "Either ProductCode must be specified or RecalculateAll must be true" } });
        }

        var response = new RecalculatePurchasePriceResponse();
        List<MaterialBomReference> bomReferences;

        if (!string.IsNullOrEmpty(request.ProductCode))
        {
            // Single product recalculation
            var product = await _materialCatalog.GetByIdAsync(request.ProductCode, cancellationToken);
            if (product == null)
            {
                _logger.LogWarning("Product with code '{ProductCode}' not found", request.ProductCode);
                return new RecalculatePurchasePriceResponse(ErrorCodes.CatalogItemNotFound, new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
            }

            if (!product.HasBoM || !product.BoMId.HasValue)
            {
                _logger.LogWarning("Product '{ProductCode}' does not have BoM", request.ProductCode);
                return new RecalculatePurchasePriceResponse(ErrorCodes.InvalidValue, new Dictionary<string, string> { { "ProductCode", request.ProductCode }, { "Message", $"Product {request.ProductCode} does not have BoM" } });
            }

            bomReferences = new List<MaterialBomReference>
            {
                new MaterialBomReference
                {
                    ProductCode = product.ProductCode,
                    BoMId = product.BoMId.Value,
                }
            };
            _logger.LogInformation("Processing single product: {ProductCode}", request.ProductCode);
        }
        else
        {
            // All products with BoM recalculation
            bomReferences = (await _materialCatalog.GetMaterialsWithBomAsync(cancellationToken)).ToList();
            _logger.LogInformation("Processing {Count} products with BoM", bomReferences.Count);
        }

        response.TotalCount = bomReferences.Count;

        // Process each product
        foreach (var bom in bomReferences)
        {
            try
            {
                _logger.LogDebug("Recalculating price for product {ProductCode} with BoMId {BoMId}",
                    bom.ProductCode, bom.BoMId);

                await _productPriceClient.RecalculatePurchasePrice(bom.BoMId, cancellationToken);

                response.ProcessedProducts.Add(new ProductRecalculationResult
                {
                    ProductCode = bom.ProductCode,
                    Success = true
                });

                response.SuccessCount++;
                _logger.LogDebug("Successfully recalculated price for product {ProductCode}", bom.ProductCode);
            }
            catch (Exception ex)
            {
                response.FailedCount++;
                var errorMessage = ex.Message;

                response.ProcessedProducts.Add(new ProductRecalculationResult
                {
                    ProductCode = bom.ProductCode,
                    Success = false,
                    ErrorCode = ErrorCodes.Exception,
                    Params = new Dictionary<string, string>
                    {
                        { "message", errorMessage },
                        { "exceptionType", ex.GetType().Name }
                    }
                });

                _logger.LogError(ex, "Failed to recalculate price for product {ProductCode}: {ErrorMessage}",
                    bom.ProductCode, errorMessage);
            }
        }

        _logger.LogInformation("Price recalculation completed - Success: {SuccessCount}, Failed: {FailedCount}, Total: {TotalCount}",
            response.SuccessCount, response.FailedCount, response.TotalCount);

        return response;
    }
}