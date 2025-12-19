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

    // Margin levels - structured breakdown
    public MarginLevelDto M0 { get; set; } = new();  // Direct material margin
    public MarginLevelDto M1_A { get; set; } = new();  // Manufacturing margin (economic baseline)
    public MarginLevelDto M1_B { get; set; } = new();  // Manufacturing margin (actual monthly cost)
    public MarginLevelDto M2 { get; set; } = new();  // Sales & marketing margin
    public MarginLevelDto M3 { get; set; } = new();  // Net profitability

    // Current month cost components (used by CatalogDetail and other components)
    // Historical data for charts (13 months)
    public List<MonthlyMarginDto> MonthlyHistory { get; set; } = new();
}