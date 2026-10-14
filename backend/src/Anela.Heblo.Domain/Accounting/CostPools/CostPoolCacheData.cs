namespace Anela.Heblo.Domain.Accounting.CostPools;

/// <summary>
/// Immutable wrapper for cached cost pool totals with metadata.
/// Mirrors CostCacheData in the Catalog module.
/// </summary>
public class CostPoolCacheData
{
    /// <summary>Monthly totals for every pool across the cached window.</summary>
    public IReadOnlyList<MonthlyCostPool> Pools { get; init; } = Array.Empty<MonthlyCostPool>();

    /// <summary>Timestamp when the cache was last successfully updated.</summary>
    public DateTime LastUpdated { get; init; }

    /// <summary>Start date of the cached window.</summary>
    public DateOnly DataFrom { get; init; }

    /// <summary>End date of the cached window.</summary>
    public DateOnly DataTo { get; init; }

    /// <summary>True once the cache has been successfully hydrated at least once.</summary>
    public bool IsHydrated { get; init; }

    /// <summary>Creates empty data for cold-start scenarios.</summary>
    public static CostPoolCacheData Empty() => new()
    {
        Pools = Array.Empty<MonthlyCostPool>(),
        LastUpdated = DateTime.MinValue,
        DataFrom = DateOnly.MinValue,
        DataTo = DateOnly.MinValue,
        IsHydrated = false
    };

    /// <summary>
    /// True when this cached window fully contains the requested range.
    /// </summary>
    public bool Covers(DateOnly from, DateOnly to) =>
        IsHydrated && DataFrom <= from && DataTo >= to;
}
