using Anela.Heblo.Domain.Accounting.CostPools;
using Microsoft.Extensions.Caching.Memory;

namespace Anela.Heblo.Application.Shared.CostPools;

/// <summary>
/// In-memory cache for cost pool totals.
/// Pure storage layer - business logic resides in CostPoolService.
/// </summary>
public class CostPoolCache : ICostPoolCache
{
    private const string CacheKey = "CostPoolCache_Data";
    private readonly IMemoryCache _memoryCache;

    public CostPoolCache(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public bool IsHydrated => _memoryCache.TryGetValue(CacheKey, out _);

    public Task<CostPoolCacheData> GetCachedDataAsync(CancellationToken ct = default)
    {
        if (_memoryCache.TryGetValue(CacheKey, out CostPoolCacheData? cachedData) && cachedData != null)
        {
            return Task.FromResult(cachedData);
        }

        return Task.FromResult(CostPoolCacheData.Empty());
    }

    public Task SetCachedDataAsync(CostPoolCacheData data, CancellationToken ct = default)
    {
        _memoryCache.Set(CacheKey, data);
        return Task.CompletedTask;
    }
}
