using System.Diagnostics;
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

    public async Task<ProductWeightRecalculationResult> RecalculateAllProductWeights(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var startTime = DateTime.UtcNow;
        var result = new ProductWeightRecalculationResult
        {
            StartTime = startTime
        };

        _logger.LogInformation("Starting product weight recalculation for all products");

        try
        {
            var allProducts = await _catalogRepository.GetAllAsync(cancellationToken);
            var products = allProducts.Where(w => w.Type == ProductType.Product).ToList();

            result.ProcessedCount = products.Count;

            foreach (var product in products)
            {
                try
                {
                    var newWeight = await _productWeightClient.RefreshProductWeight(product.ProductCode, cancellationToken);
                    if (newWeight != null)
                        product.NetWeight = newWeight;
                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    var errorMessage = $"Failed to recalculate weight for product {product.ProductCode}: {ex.Message}";
                    _logger.LogError(ex, "Failed to recalculate weight for product {ProductCode}", product.ProductCode);
                    result.ErrorCount++;
                    result.ErrorMessages.Add(errorMessage);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Product weight recalculation was cancelled after processing {ProcessedCount} products", result.SuccessCount + result.ErrorCount);
                    break;
                }
            }

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.EndTime = DateTime.UtcNow;

            _logger.LogInformation("Product weight recalculation completed. Success: {SuccessCount}, Errors: {ErrorCount}, Duration: {Duration}",
                result.SuccessCount, result.ErrorCount, result.Duration);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete product weight recalculation");

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.EndTime = DateTime.UtcNow;
            result.ErrorMessages.Add($"Critical error: {ex.Message}");
            result.ErrorCount++;

            return result;
        }
    }

    public async Task<ProductWeightRecalculationResult> RecalculateProductWeight(string productCode, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var startTime = DateTime.UtcNow;
        var result = new ProductWeightRecalculationResult
        {
            StartTime = startTime,
            ProcessedCount = 1
        };

        _logger.LogInformation("Starting product weight recalculation for product {ProductCode}", productCode);

        try
        {
            var product = await _catalogRepository.GetByIdAsync(productCode, cancellationToken);
            if (product == null)
            {
                var errorMessage = $"Product with code '{productCode}' not found";
                result.ErrorMessages.Add(errorMessage);
                result.ErrorCount = 1;
                result.ProcessedCount = 0;
                _logger.LogWarning(errorMessage);
                return result;
            }

            if (product.Type != ProductType.Product)
            {
                var errorMessage = $"Product '{productCode}' is not of type 'Product'";
                result.ErrorMessages.Add(errorMessage);
                result.ErrorCount = 1;
                result.ProcessedCount = 0;
                _logger.LogWarning(errorMessage);
                return result;
            }

            var newWeight = await _productWeightClient.RefreshProductWeight(productCode, cancellationToken);
            if (newWeight != null)
            {
                product.NetWeight = newWeight;
                result.SuccessCount = 1;
                _logger.LogInformation("Successfully recalculated weight for product {ProductCode}: {Weight}", productCode, newWeight);
            }
            else
            {
                var errorMessage = $"Failed to calculate weight for product '{productCode}' - no weight returned";
                result.ErrorMessages.Add(errorMessage);
                result.ErrorCount = 1;
                _logger.LogWarning(errorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            var errorMessage = $"Failed to recalculate weight for product {productCode}: {ex.Message}";
            _logger.LogError(ex, "Failed to recalculate weight for product {ProductCode}", productCode);

            result.ErrorMessages.Add(errorMessage);
            result.ErrorCount = 1;

            return result;
        }
        finally
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.EndTime = DateTime.UtcNow;
        }
    }
}