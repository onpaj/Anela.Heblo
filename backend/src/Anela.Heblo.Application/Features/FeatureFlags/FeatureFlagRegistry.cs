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
        new(FeatureFlagKeys.DoubleShotAvailabilitySync,
            Description: "When on, the double-shot availability sync sets each SA…V variant's Shoptet stock to its SA…S mirror set's stock; when off it only logs what it would do (dry run).",
            DefaultValue: false),
        new(FeatureFlagKeys.LabelPrintingEnabled,
            Description: "When on, label print jobs are sent to the physical printer; when off the physical print is skipped (label generation still runs). Off on Staging where no printer exists.",
            DefaultValue: true),
        new(FeatureFlagKeys.DoubleShotOrderSwap,
            Description: "When on, double-shot V order lines are replaced in Shoptet by their S mirror set (POST S, DELETE V); when off the swap only logs what it would do (dry run).",
            DefaultValue: false),
        new(FeatureFlagKeys.MissingInvoiceCreation,
            Description: "When on, the missing-invoice job creates a Shoptet invoice for each completed order with a non-zero total and no invoice; when off it only logs what it would create (dry run).",
            DefaultValue: false),
    ];

    public static readonly IReadOnlyDictionary<string, FeatureFlagDefinition> ByKey =
        All.ToDictionary(d => d.Key, StringComparer.Ordinal);
}
