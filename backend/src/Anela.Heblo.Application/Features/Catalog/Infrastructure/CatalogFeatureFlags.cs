namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Feature flags for catalog module functionality.
/// Used to control which features are enabled in different environments.
/// </summary>
public class CatalogFeatureFlags
{
    /// <summary>
    /// Controls whether transport box tracking is enabled.
    /// When false, an empty/null implementation is used.
    /// </summary>
    public bool IsTransportBoxTrackingEnabled { get; set; } = false;

    /// <summary>
    /// Controls whether stock taking functionality is enabled.
    /// When false, an empty/null implementation is used.
    /// </summary>
    public bool IsStockTakingEnabled { get; set; } = false;

    /// <summary>
    /// Controls whether background data refresh is enabled.
    /// Useful for testing environments where external dependencies should be avoided.
    /// </summary>
    public bool IsBackgroundRefreshEnabled { get; set; } = true;
}