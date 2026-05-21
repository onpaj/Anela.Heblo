namespace Anela.Heblo.Application.Features.FeatureFlags;

/// <summary>
/// Evaluates feature flags. Inject this in business code; never call OpenFeature SDK directly.
/// Always use FeatureFlagKeys constants for key names.
/// Evaluation order: DB override → FeatureManagement config → registry DefaultValue.
/// See docs/development/feature-flags.md.
/// </summary>
public interface IFeatureFlagChecker
{
    /// <summary>Returns the flag value, falling back to the registry default.</summary>
    Task<bool> IsEnabledAsync(string key, CancellationToken ct = default);

    /// <summary>Returns the flag value, falling back to the supplied default.</summary>
    Task<bool> IsEnabledAsync(string key, bool defaultValue, CancellationToken ct = default);
}
