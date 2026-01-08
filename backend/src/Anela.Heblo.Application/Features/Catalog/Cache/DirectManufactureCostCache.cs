using Anela.Heblo.Domain.Features.Catalog.Cache;
using Microsoft.Extensions.Caching.Memory;

namespace Anela.Heblo.Application.Features.Catalog.Cache;

/// <summary>
/// In-memory cache for M1_B (Direct Manufacturing) cost data.
/// Pure storage layer - business logic resides in DirectManufactureCostSource.
/// </summary>
public class DirectManufactureCostCache : IDirectManufactureCostCache
{
    private const string CacheKey = "DirectManufactureCostCache_Data";
    private readonly IMemoryCache _memoryCache;

    public DirectManufactureCostCache(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public bool IsHydrated => _memoryCache.TryGetValue(CacheKey, out _);

    public Task<CostCacheData> GetCachedDataAsync(CancellationToken ct = default)
    {
        if (_memoryCache.TryGetValue(CacheKey, out CostCacheData? cachedData) && cachedData != null)
        {
            return Task.FromResult(cachedData);
        }

        return Task.FromResult(CostCacheData.Empty());
    }

    public Task SetCachedDataAsync(CostCacheData data, CancellationToken ct = default)
    {
        _memoryCache.Set(CacheKey, data);
        return Task.CompletedTask;
    }
}
