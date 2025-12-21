using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Cache;

/// <summary>
/// In-memory cache for M0 (Material) cost data.
/// Pre-computes material costs from purchase history/BoM.
/// </summary>
public class MaterialCostCache : IMaterialCostCache, IDisposable
{
    private const string CacheKey = "MaterialCostCache_Data";
    private readonly IMemoryCache _memoryCache;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<MaterialCostCache> _logger;
    private readonly CostCacheOptions _options;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private bool _disposed;

    public MaterialCostCache(
        IMemoryCache memoryCache,
        ICatalogRepository catalogRepository,
        ILogger<MaterialCostCache> logger,
        IOptions<CostCacheOptions> options)
    {
        _memoryCache = memoryCache;
        _catalogRepository = catalogRepository;
        _logger = logger;
        _options = options.Value;
    }

    public Task<CostCacheData> GetCachedDataAsync(CancellationToken ct = default)
    {
        if (_memoryCache.TryGetValue(CacheKey, out CostCacheData? cachedData) && cachedData != null)
        {
            return Task.FromResult(cachedData);
        }

        // Cold start - return empty data
        _logger.LogWarning("MaterialCostCache not yet hydrated, returning empty data");
        return Task.FromResult(CostCacheData.Empty());
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // Acquire lock to prevent concurrent refreshes
        if (!await _refreshLock.WaitAsync(0, ct))
        {
            _logger.LogInformation("MaterialCostCache refresh already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting MaterialCostCache refresh");

            // Wait for catalog merge to complete before calculating costs
            await _catalogRepository.WaitForCurrentMergeAsync(ct);

            var products = await _catalogRepository.GetAllAsync(ct);
            var dateFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-_options.HistoricalDataYears));
            var dateTo = DateOnly.FromDateTime(DateTime.UtcNow);

            var productCosts = new Dictionary<string, List<MonthlyCost>>();

            foreach (var product in products)
            {
                if (string.IsNullOrEmpty(product.ProductCode))
                    continue;

                var monthlyCosts = CalculateMaterialCosts(product, dateFrom, dateTo);
                productCosts[product.ProductCode] = monthlyCosts;
            }

            var newCacheData = new CostCacheData
            {
                ProductCosts = productCosts,
                LastUpdated = DateTime.UtcNow,
                DataFrom = dateFrom,
                DataTo = dateTo,
                IsHydrated = true
            };

            // Store in cache without expiration (manual refresh only)
            _memoryCache.Set(CacheKey, newCacheData);

            _logger.LogInformation(
                "MaterialCostCache refreshed successfully: {ProductCount} products, {DateRange}",
                productCosts.Count,
                $"{dateFrom} to {dateTo}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh MaterialCostCache");
            // Keep old data on error (stale-while-revalidate)
            throw;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private List<MonthlyCost> CalculateMaterialCosts(CatalogAggregate product, DateOnly dateFrom, DateOnly dateTo)
    {
        // STUB: Returns purchase price only (per spec section 2.1)
        // Real implementation: use PurchasePriceOnlyMaterialCostSource logic
        var costs = new List<MonthlyCost>();

        if (product.PurchaseHistory == null || !product.PurchaseHistory.Any())
            return costs;

        // Calculate average purchase price per month
        var currentMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (currentMonth <= endMonth)
        {
            var monthPurchases = product.PurchaseHistory
                .Where(p => p.Date.Year == currentMonth.Year && p.Date.Month == currentMonth.Month)
                .ToList();

            if (monthPurchases.Any())
            {
                var avgPrice = monthPurchases.Average(p => p.PricePerPiece);
                costs.Add(new MonthlyCost(currentMonth, avgPrice));
            }

            currentMonth = currentMonth.AddMonths(1);
        }

        return costs;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _refreshLock?.Dispose();
            }
            _disposed = true;
        }
    }
}
