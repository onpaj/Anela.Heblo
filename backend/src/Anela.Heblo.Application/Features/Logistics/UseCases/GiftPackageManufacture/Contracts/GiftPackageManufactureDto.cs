namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;

public class GiftPackageManufactureDto
{
    public int Id { get; set; }
    public string GiftPackageCode { get; set; } = null!;
    public int QuantityCreated { get; set; }
    public bool StockOverrideApplied { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
    public List<GiftPackageManufactureItemDto> ConsumedItems { get; set; } = new();
}

public class GiftPackageManufactureItemDto
{
    public string ProductCode { get; set; } = null!;
    public int QuantityConsumed { get; set; }
}