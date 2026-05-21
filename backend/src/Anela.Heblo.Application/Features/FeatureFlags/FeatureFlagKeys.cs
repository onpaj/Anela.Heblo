namespace Anela.Heblo.Application.Features.FeatureFlags;

/// <summary>
/// String constants for all known feature flags.
/// Always use these constants — never hard-code flag key strings.
/// See docs/development/feature-flags.md.
/// </summary>
public static class FeatureFlagKeys
{
    /// <summary>Controls whether transport box tracking is enabled. Default: false.</summary>
    public const string TransportBoxTracking = "is-transport-box-tracking-enabled";

    /// <summary>Controls whether stock taking submission UI and processing is enabled. Default: false.</summary>
    public const string StockTaking = "is-stock-taking-enabled";

    /// <summary>Controls whether background data refresh is enabled. Disable in test environments. Default: true.</summary>
    public const string BackgroundRefresh = "is-background-refresh-enabled";
}
