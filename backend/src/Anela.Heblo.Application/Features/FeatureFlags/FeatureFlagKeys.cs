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

    /// <summary>
    /// When on, label print requests are sent to the physical (CUPS) printer. When off, the
    /// print is skipped but the surrounding operation still runs — e.g. material-container
    /// labels are still generated and persisted as Unassigned. Off on Staging (no printer).
    /// </summary>
    public const string LabelPrintingEnabled = "is-label-printing-enabled";

    /// <summary>
    /// When on, every shipping-label PDF served to the packing desk is moved 10 mm to the left
    /// before printing. The four direction flags add up (left + up = diagonal, left + right = none);
    /// they exist to find out at the Zebra which direction actually fixes the label placement.
    /// </summary>
    public const string LabelOffsetLeft = "is-label-offset-left-enabled";

    /// <summary>When on, every shipping-label PDF is moved 10 mm to the right. See <see cref="LabelOffsetLeft"/>.</summary>
    public const string LabelOffsetRight = "is-label-offset-right-enabled";

    /// <summary>When on, every shipping-label PDF is moved 10 mm up. See <see cref="LabelOffsetLeft"/>.</summary>
    public const string LabelOffsetUp = "is-label-offset-up-enabled";

    /// <summary>When on, every shipping-label PDF is moved 10 mm down. See <see cref="LabelOffsetLeft"/>.</summary>
    public const string LabelOffsetDown = "is-label-offset-down-enabled";
}
