using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Cache;

/// <summary>
/// In-memory cache for M1_A (Flat Manufacturing) cost data.
/// Pre-computes flat manufacturing costs based on ledger data and manufacture history.
/// </summary>
public class FlatManufactureCostCache : IFlatManufactureCostCache, IDisposable
{
    private const string CacheKey = "FlatManufactureCostCache_Data";
    private readonly IMemoryCache _memoryCache;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<FlatManufactureCostCache> _logger;
    private readonly CostCacheOptions _options;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private bool _disposed;

    public FlatManufactureCostCache(
        IMemoryCache memoryCache,
        ICatalogRepository catalogRepository,
        ILogger<FlatManufactureCostCache> logger,
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
        _logger.LogWarning("FlatManufactureCostCache not yet hydrated, returning empty data");
        return Task.FromResult(CostCacheData.Empty());
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // Acquire lock to prevent concurrent refreshes
        if (!await _refreshLock.WaitAsync(0, ct))
        {
            _logger.LogInformation("FlatManufactureCostCache refresh already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting FlatManufactureCostCache refresh");

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

                var monthlyCosts = CalculateFlatManufacturingCosts(product, dateFrom, dateTo);
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
                "FlatManufactureCostCache refreshed successfully: {ProductCount} products, {DateRange}",
                productCosts.Count,
                $"{dateFrom} to {dateTo}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh FlatManufactureCostCache");
            // Keep old data on error (stale-while-revalidate)
            throw;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private List<MonthlyCost> CalculateFlatManufacturingCosts(CatalogAggregate product, DateOnly dateFrom, DateOnly dateTo)
    {
        // STUB: Returns constant value of 15 (per spec section 2.2)
        // Real implementation: use ILedgerService + IManufactureHistoryClient
        var costs = new List<MonthlyCost>();

        var currentMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (currentMonth <= endMonth)
        {
            costs.Add(new MonthlyCost(currentMonth, 15m));
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
