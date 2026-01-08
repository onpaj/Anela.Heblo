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
}
