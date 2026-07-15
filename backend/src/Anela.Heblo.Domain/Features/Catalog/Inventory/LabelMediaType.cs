namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

/// <summary>
/// The physical label media loaded on the shared Zebra printer. Different types require the
/// operator to swap and re-calibrate the media roll before printing, so switching types is
/// tracked and confirmed. <see cref="Unknown"/> is the initial state before anything is printed.
/// </summary>
public enum LabelMediaType
{
    Unknown = 0,

    /// <summary>Continuous round die-cut stock used for lot labels (and lot-media alignment actions).</summary>
    LotRound = 1,

    /// <summary>Gap-sensing rectangular stock used for material-container barcode labels.</summary>
    MaterialContainer = 2,
}
