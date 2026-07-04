namespace Anela.Heblo.Application.Features.ShipmentLabels;

public class ShippingOption
{
    public string CarrierCode { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
}

public class CreateShipmentCommand
{
    public string OrderCode { get; set; } = null!;
    public string CarrierCode { get; set; } = null!;
    public int PackageCount { get; set; } = 1;
    public ShipmentPackage Package { get; set; } = null!;
}

public class ShipmentPackage
{
    public int WidthCm { get; set; }
    public int HeightCm { get; set; }
    public int DepthCm { get; set; }
    public int WeightGrams { get; set; }
}

public class CreatedShipment
{
    public Guid ShipmentGuid { get; set; }
    public string? Status { get; set; }
}
