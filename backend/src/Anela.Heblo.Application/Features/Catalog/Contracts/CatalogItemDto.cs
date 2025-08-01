using Anela.Heblo.Application.Domain.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

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
}