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