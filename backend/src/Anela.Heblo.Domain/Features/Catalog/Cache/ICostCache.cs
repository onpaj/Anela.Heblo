namespace Anela.Heblo.Domain.Features.Catalog.Cache;

/// <summary>
/// Base interface for cost cache services providing cached cost data.
/// </summary>
public interface ICostCache
{
    /// <summary>
    /// Get cached cost data. Returns empty data during initial hydration.
    /// </summary>
    Task<CostCacheData> GetCachedDataAsync(CancellationToken ct = default);

    /// <summary>
    /// Refresh cached data from source repositories.
    /// Uses stale-while-revalidate pattern - keeps old data during refresh.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);
}
