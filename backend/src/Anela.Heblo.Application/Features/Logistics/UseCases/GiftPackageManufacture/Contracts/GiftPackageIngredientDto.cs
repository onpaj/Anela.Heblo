namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;

public class GiftPackageIngredientDto
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public double RequiredQuantity { get; set; }
    /// <summary>
    /// Warehouse stock only (<c>StockData.WarehouseStock</c>) - what can actually be picked to
    /// build this package. Note this is NOT the same measure as <see cref="GiftPackageDto"/>'s
    /// own <c>AvailableStock</c>, which is still <c>StockData.Available</c> and also counts goods
    /// in transport and in the manufacture warehouse. Same name, two meanings, one payload.
    /// </summary>
    public double AvailableStock { get; set; }
    public string? Image { get; set; }
    public bool HasSufficientStock => AvailableStock >= RequiredQuantity;
}