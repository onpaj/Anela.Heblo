namespace Anela.Heblo.Domain.Features.DataQuality;

[Flags]
public enum LotStockReconciliationMismatch
{
    None = 0,

    /// <summary>Lot sum and ERP stock are both non-zero but differ beyond tolerance.</summary>
    SumMismatch = 1,

    /// <summary>ERP stock is present but no lots are loaded.</summary>
    MissingLots = 2,

    /// <summary>Lots are present but ERP stock is effectively zero.</summary>
    OrphanLots = 4
}
