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
        new(FeatureFlagKeys.DeliveredOrderCompletion,
            Description: "When on, the delivered-orders job changes order state to 'vyřízena' and writes the remark; when off it only logs what it would do (dry run).",
            DefaultValue: false),
        new(FeatureFlagKeys.DeliveredOrderCompletionTestSource,
            Description: "When on, the delivered-orders job polls test state 73 (Oprava-robot) instead of the production 'handed to carrier' states 70/82.",
            DefaultValue: false),
    ];

    public static readonly IReadOnlyDictionary<string, FeatureFlagDefinition> ByKey =
        All.ToDictionary(d => d.Key, StringComparer.Ordinal);
}
