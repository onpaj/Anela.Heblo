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
    /// When on, GLS shipping-label PDFs served to the packing desk are moved 10 mm to the left
    /// before printing, so they fit the shorter Zebra label stock. Other carriers are untouched.
    /// </summary>
    public const string GlsLabelOffset = "is-gls-label-offset-enabled";

    /// <summary>
    /// When on, PPL shipping-label PDFs served to the packing desk are moved 10 mm to the left
    /// before printing. Other carriers are governed by their own offset flag.
    /// </summary>
    public const string PplLabelOffset = "is-ppl-label-offset-enabled";

    /// <summary>
    /// When on, Zásilkovna (Packeta) shipping-label PDFs served to the packing desk are moved
    /// 10 mm to the left before printing. Other carriers are governed by their own offset flag.
    /// </summary>
    public const string ZasilkovnaLabelOffset = "is-zasilkovna-label-offset-enabled";
}
