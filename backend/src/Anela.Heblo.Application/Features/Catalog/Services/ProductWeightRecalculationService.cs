using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Products;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public class ProductWeightRecalculationService : IProductWeightRecalculationService
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IProductWeightClient _productWeightClient;
    private readonly ILogger<ProductWeightRecalculationService> _logger;

    public ProductWeightRecalculationService(
        ICatalogRepository catalogRepository,
        IProductWeightClient productWeightClient,
        ILogger<ProductWeightRecalculationService> logger)
    {
        _catalogRepository = catalogRepository;
        _productWeightClient = productWeightClient;
        _logger = logger;
    }

    public async Task RecalculateAllProductWeights(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting product weight recalculation for all products");

        try
        {
            var allProducts = await _catalogRepository.GetAllAsync(cancellationToken);
            var products = allProducts.Where(w => w.Type == ProductType.Product);

            var successCount = 0;
            var errorCount = 0;

            foreach (var product in products)
            {
                try
                {
                    var newWeight = await _productWeightClient.RefreshProductWeight(product.ProductCode, cancellationToken);
                    if(newWeight != null)
                        product.NetWeight = newWeight;
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to recalculate weight for product {ProductCode}", product.ProductCode);
                    errorCount++;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Product weight recalculation was cancelled after processing {ProcessedCount} products", successCount + errorCount);
                    break;
                }
            }

            _logger.LogInformation("Product weight recalculation completed. Success: {SuccessCount}, Errors: {ErrorCount}", successCount, errorCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete product weight recalculation");
            throw;
        }
    }
}