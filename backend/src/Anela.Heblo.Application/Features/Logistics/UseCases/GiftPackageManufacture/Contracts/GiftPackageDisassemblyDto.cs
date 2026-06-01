namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;

public class GiftPackageDisassemblyDto
{
    public string GiftPackageCode { get; set; } = null!;
    public int QuantityDisassembled { get; set; }
    public DateTime DisassembledAt { get; set; }
    public string DisassembledBy { get; set; } = null!;
    public List<GiftPackageDisassemblyItemDto> ReturnedComponents { get; set; } = new();
}

public class GiftPackageDisassemblyItemDto
{
    public string ProductCode { get; set; } = null!;
    public int QuantityReturned { get; set; }
}
