namespace Anela.Heblo.Application.Features.FeatureFlags;

/// <summary>
/// String constants for all known feature flags.
/// Always use these constants — never hard-code flag key strings.
/// See docs/development/feature-flags.md.
/// </summary>
public static class FeatureFlagKeys
{
    /// <summary>
    /// When on, the delivered-orders job applies changes (order state → "vyřízena" and the
    /// remark). When off, the job runs in dry-run mode and only logs what it would do.
    /// </summary>
    public const string DeliveredOrderCompletion = "is-delivered-order-completion-enabled";

    /// <summary>
    /// When on, the delivered-orders job polls the test source states (73 "Oprava-robot")
    /// instead of the production "handed to carrier" states — lets the whole pipeline be
    /// exercised on a controlled set of orders. When off, the production states are used.
    /// </summary>
    public const string DeliveredOrderCompletionTestSource = "is-delivered-order-completion-test-source-enabled";
}
