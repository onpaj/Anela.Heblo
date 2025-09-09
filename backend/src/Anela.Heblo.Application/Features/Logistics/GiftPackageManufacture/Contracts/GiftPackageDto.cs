namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Contracts;

public class GiftPackageDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int AvailableStock { get; set; }
    public decimal DailySales { get; set; }
    public int OverstockLimit { get; set; }
    public List<GiftPackageIngredientDto>? Ingredients { get; set; }
}

public class GiftPackageIngredientDto
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public double RequiredQuantity { get; set; }
    public double AvailableStock { get; set; }
    public bool HasSufficientStock => AvailableStock >= RequiredQuantity;
}