namespace Anela.Heblo.Xcc.Services.BackgroundRefresh;

/// <summary>
/// Configuration options for background services
/// </summary>
public class BackgroundServicesOptions
{
    public const string SectionName = "BackgroundServices";

    /// <summary>
    /// Enables or disables the tier-based hydration process.
    /// When false, hydration will not run (useful for testing environments).
    /// </summary>
    public bool EnableHydration { get; set; } = true;

    /// <summary>
    /// Hydration tier at/below which the app is considered ready (/health/ready → 200).
    /// Readiness is reached once all tiers with tier &lt;= ReadinessTier complete.
    /// Higher tiers keep hydrating in the background and do not block readiness.
    /// 0 = ready immediately (no tier is required). Default 1.
    /// </summary>
    public int ReadinessTier { get; set; } = 1;
}
