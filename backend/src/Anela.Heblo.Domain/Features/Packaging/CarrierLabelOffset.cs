using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Domain.Features.Packaging;

/// <summary>
/// How far a carrier's shipping-label PDF is moved before it is printed at the packing desk.
/// X is towards the PDF page's right edge, Y towards its top edge, both in whole millimetres
/// (negative = left / down). Axes follow the PDF as the carrier sends it, not the physical label,
/// because the print dialog may turn a landscape PDF onto the portrait Zebra label.
/// </summary>
public class CarrierLabelOffset
{
    /// <summary>Largest move allowed in either direction on either axis.</summary>
    public const int MaxOffsetMm = 30;

    /// <summary>Carriers whose labels are printed at the packing desk and can be offset.</summary>
    public static readonly IReadOnlyList<Carriers> LabelCarriers = [Carriers.Zasilkovna, Carriers.PPL, Carriers.GLS];

    public Carriers Carrier { get; private set; }
    public int OffsetXMm { get; private set; }
    public int OffsetYMm { get; private set; }
    public DateTime ModifiedAt { get; private set; }
    public string ModifiedBy { get; private set; } = null!;

    private CarrierLabelOffset() { }

    public CarrierLabelOffset(Carriers carrier, int offsetXMm, int offsetYMm, string modifiedBy)
    {
        Carrier = carrier;
        OffsetXMm = offsetXMm;
        OffsetYMm = offsetYMm;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTime.UtcNow;
    }

    internal void UpdateOffset(int offsetXMm, int offsetYMm, string modifiedBy)
    {
        OffsetXMm = offsetXMm;
        OffsetYMm = offsetYMm;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTime.UtcNow;
    }
}
