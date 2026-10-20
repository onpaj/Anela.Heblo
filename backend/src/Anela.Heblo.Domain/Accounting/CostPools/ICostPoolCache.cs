namespace Anela.Heblo.Domain.Accounting.CostPools;

/// <summary>
/// Storage for computed cost pool totals. Pure storage layer -
/// the bucketing logic lives in CostPoolService.
/// </summary>
public interface ICostPoolCache
{
    /// <summary>Gets cached totals. Returns unhydrated empty data when nothing is stored.</summary>
    Task<CostPoolCacheData> GetCachedDataAsync(CancellationToken ct = default);

    /// <summary>Stores computed totals, replacing whatever was there.</summary>
    Task SetCachedDataAsync(CostPoolCacheData data, CancellationToken ct = default);

    /// <summary>True when the cache holds data.</summary>
    bool IsHydrated { get; }
}
