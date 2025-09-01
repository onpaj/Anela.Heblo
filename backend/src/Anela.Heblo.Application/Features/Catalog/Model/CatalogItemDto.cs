using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Model;

public class CatalogItemDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public ProductType Type { get; set; }
    public StockDto Stock { get; set; } = new();
    public PriceDto Price { get; set; } = new();
    public PropertiesDto Properties { get; set; } = new();
    public string Location { get; set; } = string.Empty;
    public string MinimalOrderQuantity { get; set; } = string.Empty;
    public double MinimalManufactureQuantity { get; set; }
    public double ManufactureDifficulty { get; set; }
    public decimal MarginPercentage { get; set; }
    public decimal MarginAmount { get; set; }
    public string? Note { get; set; }
}