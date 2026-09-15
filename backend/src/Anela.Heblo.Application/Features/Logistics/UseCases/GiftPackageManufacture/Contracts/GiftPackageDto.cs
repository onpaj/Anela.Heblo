namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;

public class GiftPackageDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    /// <summary>
    /// The package's own stock as <c>StockData.Available</c> - warehouse plus transport plus the
    /// manufacture warehouse. Correct for the planning figures it feeds (severity, suggested
    /// quantity), but do not use it to authorise a stock-down; see
    /// <see cref="GiftPackageIngredientDto.AvailableStock"/>.
    /// </summary>
    public int AvailableStock { get; set; }
    public decimal DailySales { get; set; }
    public int OverstockOptimal { get; set; }
    public int OverstockMinimal { get; set; }
    public int SuggestedQuantity { get; set; }
    public GiftPackageSeverity Severity { get; set; }
    public decimal StockCoveragePercent { get; set; }
    public List<GiftPackageIngredientDto>? Ingredients { get; set; }
}