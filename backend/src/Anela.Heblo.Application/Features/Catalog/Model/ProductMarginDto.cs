namespace Anela.Heblo.Application.Features.Catalog.Model;

public class ProductMarginDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal? PriceWithoutVat { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? ManufactureCost { get; set; }
    public decimal? MaterialCost { get; set; }
    public double ManufactureDifficulty { get; set; }
    public decimal? AverageMargin { get; set; }
}