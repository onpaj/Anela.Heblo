namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

/// <summary>
/// Which way the printed text walks across the label face over a run, as reported by the
/// operator in the calibration wizard. Deliberately starts at 1 so an unset value is
/// invalid rather than silently meaning <see cref="Up"/>.
/// </summary>
public enum LabelDriftDirection
{
    /// <summary>Text climbs towards the top edge: the media advances too far per label.</summary>
    Up = 1,

    /// <summary>Text sinks towards the bottom edge: the media advances too little per label.</summary>
    Down = 2,
}
