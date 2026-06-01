namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;

public class GiftPackageIngredientDto
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public double RequiredQuantity { get; set; }
    public double AvailableStock { get; set; }
    public string? Image { get; set; }
    public bool HasSufficientStock => AvailableStock >= RequiredQuantity;
}