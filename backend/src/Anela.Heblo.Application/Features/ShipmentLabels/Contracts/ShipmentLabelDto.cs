namespace Anela.Heblo.Application.Features.ShipmentLabels.Contracts;

public class ShipmentLabelDto
{
    public Guid ShipmentGuid { get; set; }
    public string PackageName { get; set; } = null!;
    public string? LabelUrl { get; set; }
    public string? LabelZpl { get; set; }
    public bool HasPdf => LabelUrl is not null;
    public bool HasZpl => LabelZpl is not null;
    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }
}
