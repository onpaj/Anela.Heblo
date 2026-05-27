namespace Anela.Heblo.Domain.Features.Packaging;

public class Package
{
    public int Id { get; set; }
    public string OrderCode { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string PackageNumber { get; set; } = null!;
    public string? TrackingNumber { get; set; }
    public string ShippingProviderCode { get; set; } = null!;
    public string? ShippingProviderName { get; set; }
    public Guid ShipmentGuid { get; set; }
    public DateTimeOffset PackedAt { get; set; }
    public string? PackedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
