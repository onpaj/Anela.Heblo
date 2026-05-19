namespace Anela.Heblo.Application.Features.ShipmentLabels;

public class ShipmentLabel
{
    public Guid ShipmentGuid { get; set; }
    public string OrderCode { get; set; } = null!;
    public string PackageName { get; set; } = null!;
    public string? LabelUrl { get; set; }
    public string? LabelZpl { get; set; }
    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }
}
