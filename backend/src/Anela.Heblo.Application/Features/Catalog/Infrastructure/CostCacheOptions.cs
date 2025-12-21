namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Configuration options for cost cache services.
/// </summary>
public class CostCacheOptions
{
    public const string SectionName = "CostCache";

    /// <summary>
    /// Interval for periodic refresh (default: 6 hours).
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Rolling window size for M1_A calculation (default: 12 months).
    /// </summary>
    public int M1ARollingWindowMonths { get; set; } = 12;

    /// <summary>
    /// Hydration tier for cost cache services (default: 2 - after catalog refresh).
    /// </summary>
    public int HydrationTier { get; set; } = 2;

    /// <summary>
    /// Number of years of historical data to cache (default: 2).
    /// </summary>
    public int HistoricalDataYears { get; set; } = 2;

    /// <summary>
    /// Minimum date for M2 margin data availability.
    /// </summary>
    public DateOnly MinM2DataDate { get; set; } = new DateOnly(2025, 1, 1);
}
