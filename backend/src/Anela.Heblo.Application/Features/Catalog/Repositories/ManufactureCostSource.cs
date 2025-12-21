using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Repositories;

public class ManufactureCostSource : IFlatManufactureCostSource
{
    private readonly IFlatManufactureCostCache _cache;
    private readonly ILogger<ManufactureCostSource> _logger;

    public ManufactureCostSource(
        IFlatManufactureCostCache cache,
        ILogger<ManufactureCostSource> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(
        List<string>? productCodes = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheData = await _cache.GetCachedDataAsync(cancellationToken);

            if (!cacheData.IsHydrated)
            {
                _logger.LogWarning("FlatManufactureCostCache not hydrated, returning empty costs");
                return new Dictionary<string, List<MonthlyCost>>();
            }

            // Filter by productCodes if specified
            if (productCodes == null || !productCodes.Any())
            {
                return cacheData.ProductCosts;
            }

            return cacheData.ProductCosts
                .Where(kvp => productCodes.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting flat manufacture costs from cache");
            throw;
        }
    }
}