namespace Anela.Heblo.Application.Features.FeatureFlags;

/// <summary>
/// Single source of truth for all feature flags in the system.
/// To add a new flag: (1) add a constant to FeatureFlagKeys, (2) add an entry here,
/// (3) add the same default to appsettings.json under "FeatureManagement:".
/// See docs/development/feature-flags.md.
/// </summary>
public static class FeatureFlagRegistry
{
    public static readonly IReadOnlyList<FeatureFlagDefinition> All =
    [
        new(FeatureFlagKeys.TransportBoxTracking,
            Description: "Controls whether transport box tracking is enabled.",
            DefaultValue: false),
        new(FeatureFlagKeys.StockTaking,
            Description: "Controls whether stock taking submission UI and processing is enabled.",
            DefaultValue: false),
        new(FeatureFlagKeys.BackgroundRefresh,
            Description: "Controls whether background data refresh is enabled. Disable in test environments.",
            DefaultValue: true),
    ];
}
