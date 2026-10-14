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
    /// When on, the double-shot availability sync writes each SA…V variant's stock to match its
    /// SA…S mirror set. When off, the job runs in dry-run mode and only logs what it would change.
    /// </summary>
    public const string DoubleShotAvailabilitySync = "is-doubleshot-availability-sync-enabled";

    /// <summary>
    /// When on, label print requests are sent to the physical (CUPS) printer. When off, the
    /// print is skipped but the surrounding operation still runs — e.g. material-container
    /// labels are still generated and persisted as Unassigned. Off on Staging (no printer).
    /// </summary>
    public const string LabelPrintingEnabled = "is-label-printing-enabled";

    /// <summary>
    /// When on, the double-shot swap job and the picking-list pre-step really replace V order lines
    /// with their S set in Shoptet. When off, they only log what they would do (dry run).
    /// </summary>
    public const string DoubleShotOrderSwap = "is-double-shot-order-swap-enabled";
}
