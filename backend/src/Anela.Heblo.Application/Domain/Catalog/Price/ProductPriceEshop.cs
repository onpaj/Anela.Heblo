namespace Anela.Heblo.Application.Domain.Catalog.Price;

public class ProductPriceEshop
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal? PriceWithVat { get; set; }
    public decimal? PurchasePrice { get; set; }
}