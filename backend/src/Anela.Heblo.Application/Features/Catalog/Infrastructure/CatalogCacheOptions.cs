namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

public class CatalogCacheOptions
{
    public const string SectionName = "CatalogCache";

    /// <summary>
    /// Delay before executing merge after last invalidation
    /// </summary>
    public TimeSpan DebounceDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum time to wait before forcing a merge (even if invalidations keep coming)
    /// </summary>
    public TimeSpan MaxMergeInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long cached data is considered valid without any updates
    /// </summary>
    public TimeSpan CacheValidityPeriod { get; set; } = TimeSpan.FromHours(4);

    /// <summary>
    /// Whether to return stale data during merge operations
    /// </summary>
    public bool AllowStaleDataDuringMerge { get; set; } = true;

    /// <summary>
    /// How long to keep stale cache as fallback
    /// </summary>
    public TimeSpan StaleDataRetentionPeriod { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether to use background merge optimization
    /// </summary>
    public bool EnableBackgroundMerge { get; set; } = true;
}