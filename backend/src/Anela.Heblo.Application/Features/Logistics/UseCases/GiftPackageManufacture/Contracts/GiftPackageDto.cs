using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;

public class GiftPackageDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int AvailableStock { get; set; }
    public decimal DailySales { get; set; }
    public int OverstockOptimal { get; set; }
    public int OverstockMinimal { get; set; }
    public int SuggestedQuantity { get; set; }
    public StockSeverity Severity { get; set; }
    public decimal StockCoveragePercent { get; set; }
    public List<GiftPackageIngredientDto>? Ingredients { get; set; }
}