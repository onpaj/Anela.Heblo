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

    // Margin levels - structured breakdown (M0-M2)
    public MarginLevelDto M0 { get; set; } = new();  // Direct material margin
    public MarginLevelDto M1 { get; set; } = new();  // Manufacturing margin
    public MarginLevelDto M2 { get; set; } = new();  // Sales & marketing margin

    // Current month cost components (used by CatalogDetail and other components)
    // Historical data for charts (13 months)
    public List<MonthlyMarginDto> MonthlyHistory { get; set; } = new();
}