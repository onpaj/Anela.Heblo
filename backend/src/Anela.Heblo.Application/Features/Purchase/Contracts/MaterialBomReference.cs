namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public sealed class MaterialBomReference
{
    public required string ProductCode { get; init; }
    public required int BoMId { get; init; }

    /// <summary>
    /// True when the BoM owner is a semi-product. Semi-product BoMs are recalculated before
    /// product BoMs, because Flexi's roll-up reads each component's stored purchase price.
    /// </summary>
    public bool IsSemiProduct { get; init; }

    /// <summary>
    /// True when the BoM owner is a set. Sets are assembled from finished products, so their BoM
    /// is recalculated after product BoMs, because Flexi's roll-up reads each component's stored
    /// purchase price.
    /// </summary>
    public bool IsSet { get; init; }
}
