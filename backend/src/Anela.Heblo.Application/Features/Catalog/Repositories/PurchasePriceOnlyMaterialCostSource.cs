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
    private readonly ILogger<PurchasePriceOnlyMaterialCostSource> _logger;

    public PurchasePriceOnlyMaterialCostSource(
        ICatalogRepository catalogRepository,
        TimeProvider timeProvider,
        ILogger<PurchasePriceOnlyMaterialCostSource> logger)
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
        var result = new Dictionary<string, List<MonthlyCost>>();

        // Default to last 12 months if not specified
        var now = _timeProvider.GetUtcNow().DateTime;
        var effectiveDateFrom = dateFrom ?? DateOnly.FromDateTime(now.AddMonths(-12));
        var effectiveDateTo = dateTo ?? DateOnly.FromDateTime(now);

        // Generate list of months in range
        var months = GenerateMonths(effectiveDateFrom, effectiveDateTo);

        foreach (var product in products)
        {
            if (string.IsNullOrEmpty(product.ProductCode))
                continue;

            // Get current purchase price
            var purchasePrice = product.CurrentPurchasePrice ?? 0m;

            // Generate monthly costs with constant purchase price
            var monthlyCosts = months.Select(month => new MonthlyCost(month, purchasePrice)).ToList();

            result[product.ProductCode] = monthlyCosts;
        }

        return await Task.FromResult(result);
    }

    private static List<DateTime> GenerateMonths(DateOnly dateFrom, DateOnly dateTo)
    {
        var months = new List<DateTime>();
        var current = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var end = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (current <= end)
        {
            months.Add(current);
            current = current.AddMonths(1);
        }

        return months;
    }
}