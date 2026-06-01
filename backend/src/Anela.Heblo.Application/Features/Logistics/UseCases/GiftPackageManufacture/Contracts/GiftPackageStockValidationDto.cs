namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;

public class GiftPackageStockValidationDto
{
    public string GiftPackageCode { get; set; } = null!;
    public int RequestedQuantity { get; set; }
    public bool HasSufficientStock { get; set; }
    public List<StockShortageDto> Shortages { get; set; } = new();
}

public class StockShortageDto
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public double RequiredQuantity { get; set; }
    public double AvailableStock { get; set; }
    public double ShortageAmount => RequiredQuantity - AvailableStock;
}