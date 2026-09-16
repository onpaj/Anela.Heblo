namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

/// <summary>
/// How quickly the printed text walks out of place, as reported by the operator in the
/// calibration wizard. The two rates are the reference points the wizard's buttons are
/// labelled with, not free-form measurements. Starts at 1 so an unset value is invalid.
/// </summary>
public enum LabelDriftSpeed
{
    /// <summary>Visibly out of place within roughly 20 labels.</summary>
    Fast = 1,

    /// <summary>Visibly out of place only after roughly 50 labels.</summary>
    Slow = 2,
}
