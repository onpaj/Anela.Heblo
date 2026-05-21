namespace Anela.Heblo.Application.Features.FeatureFlags;

/// <summary>
/// Single source of truth for all feature flags in the system.
/// To add a new flag: (1) add a constant to FeatureFlagKeys, (2) add an entry here,
/// (3) add the same default to appsettings.json under "FeatureManagement:".
/// See docs/development/feature-flags.md.
/// </summary>
public static class FeatureFlagRegistry
{
    public static readonly IReadOnlyList<FeatureFlagDefinition> All = [];

    public static readonly IReadOnlyDictionary<string, FeatureFlagDefinition> ByKey =
        All.ToDictionary(d => d.Key, StringComparer.Ordinal);
}
