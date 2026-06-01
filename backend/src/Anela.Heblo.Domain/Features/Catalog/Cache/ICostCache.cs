namespace Anela.Heblo.Domain.Features.Catalog.Cache;

/// <summary>
/// Base interface for cost cache services providing thin wrapper over IMemoryCache.
/// This is a pure storage layer - business logic resides in cost sources.
/// </summary>
public interface ICostCache
{
    /// <summary>
    /// Get cached cost data. Returns empty data if not hydrated.
    /// </summary>
    Task<CostCacheData> GetCachedDataAsync(CancellationToken ct = default);

    /// <summary>
    /// Set cached cost data.
    /// </summary>
    Task SetCachedDataAsync(CostCacheData data, CancellationToken ct = default);

    /// <summary>
    /// Indicates whether the cache has been hydrated with data.
    /// </summary>
    bool IsHydrated { get; }
}
