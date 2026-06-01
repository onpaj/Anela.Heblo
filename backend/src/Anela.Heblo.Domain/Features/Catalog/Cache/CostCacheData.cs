using Anela.Heblo.Domain.Features.Catalog.ValueObjects;

namespace Anela.Heblo.Domain.Features.Catalog.Cache;

/// <summary>
/// Immutable wrapper for cached cost data with metadata.
/// </summary>
public class CostCacheData
{
    /// <summary>
    /// Pre-computed cost data per product code.
    /// Key = productCode, Value = monthly costs for that product.
    /// </summary>
    public Dictionary<string, List<MonthlyCost>> ProductCosts { get; init; } = new();

    /// <summary>
    /// Timestamp when cache was last successfully updated.
    /// </summary>
    public DateTime LastUpdated { get; init; }

    /// <summary>
    /// Start date of cached data range.
    /// </summary>
    public DateOnly DataFrom { get; init; }

    /// <summary>
    /// End date of cached data range.
    /// </summary>
    public DateOnly DataTo { get; init; }

    /// <summary>
    /// True if cache has been successfully hydrated at least once.
    /// </summary>
    public bool IsHydrated { get; init; }

    /// <summary>
    /// Creates empty cache data for cold start scenarios.
    /// </summary>
    public static CostCacheData Empty() => new()
    {
        ProductCosts = new Dictionary<string, List<MonthlyCost>>(),
        LastUpdated = DateTime.MinValue,
        DataFrom = DateOnly.MinValue,
        DataTo = DateOnly.MinValue,
        IsHydrated = false
    };
}
