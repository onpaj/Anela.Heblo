namespace Anela.Heblo.Application.Domain.Catalog.Attributes;

public class CatalogAttributes
{
    public int ProductId { get; set; }

    public string ProductCode { get; set; }

    public int OptimalStockDays { get; set; } = 0;
    public decimal StockMin { get; set; } = 0;
    public int BatchSize { get; set; } = 0;

    public int MinimalManufactureQuantity { get; set; } = 0;
    public ProductType ProductType { get; set; }
    public int[] SeasonMonthsArray { get; set; } = Array.Empty<int>();
}