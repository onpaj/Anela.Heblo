using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Repositories;

// Loads material cost from manufacture history or purchase price directly
public class PurchasePriceOnlyMaterialCostSource : IMaterialCostSource
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CatalogMaterialCostRepository> _logger;

    public PurchasePriceOnlyMaterialCostSource(
        ICatalogRepository catalogRepository,
        TimeProvider timeProvider,
        ILogger<CatalogMaterialCostRepository> logger)
    {
        _catalogRepository = catalogRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(List<string>? productCodes = null, DateOnly? dateFrom = null, DateOnly? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IEnumerable<CatalogAggregate> products;
            if (productCodes?.Count == 1)
            {
                var product = await _catalogRepository.GetByIdAsync(productCodes.Single(), cancellationToken);
                products = [product];
            }
            else
            {
                products = await _catalogRepository.GetAllAsync(cancellationToken);
                // Filter by product codes if specified
                if (productCodes != null && productCodes.Count > 0)
                {
                    products = products.Where(p => p.ProductCode != null && productCodes.Contains(p.ProductCode)).ToList();
                }
            }

            return await GetCostsAsync(products, dateFrom, dateTo, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating material costs");
            throw;
        }
    }

    private async Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(IEnumerable<CatalogAggregate> products,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        // TODO implement loading material costs from purchase price history
    }
}