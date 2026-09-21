namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class ProductMarginDto
{
    // Basic product properties
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal? PriceWithoutVat { get; set; }
    public decimal? PurchasePrice { get; set; }

    public double ManufactureDifficulty { get; set; }
    public bool PriceWithoutVatIsFromEshop { get; set; } = false;

    // Margin levels - cumulative breakdown (M0-M3); each level adds one cost layer
    public MarginLevelDto M0 { get; set; } = new();  // Direct material margin
    public MarginLevelDto M1 { get; set; } = new();  // + manufacturing
    public MarginLevelDto M2 { get; set; } = new();  // + sales & marketing
    public MarginLevelDto M3 { get; set; } = new();  // + overhead (net profitability)

    // Current month cost components (used by CatalogDetail and other components)
    // Historical data for charts (13 months)
    public List<MonthlyMarginDto> MonthlyHistory { get; set; } = new();
}