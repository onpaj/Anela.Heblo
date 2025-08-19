namespace Anela.Heblo.Application.Features.Catalog.Model;

public class ProductMarginDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal? PriceWithoutVat { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? AverageMaterialCost { get; set; } // Average MaterialCost from ManufactureCostHistory (excluding zero values)
    public decimal? AverageHandlingCost { get; set; } // Average HandlingCost from ManufactureCostHistory (excluding zero values)
    public double ManufactureDifficulty { get; set; }
    public decimal MarginPercentage { get; set; } // Direct from CatalogAggregate.MarginPercentage
    public decimal MarginAmount { get; set; } // Direct from CatalogAggregate.MarginAmount
}