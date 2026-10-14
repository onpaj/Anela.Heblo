using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.Packaging.Contracts;

public class CarrierLabelOffsetDto
{
    public Carriers Carrier { get; set; }

    /// <summary>Millimetres towards the PDF page's right edge (negative = left).</summary>
    public int OffsetXMm { get; set; }

    /// <summary>Millimetres towards the PDF page's top edge (negative = down).</summary>
    public int OffsetYMm { get; set; }

    public DateTime? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}
