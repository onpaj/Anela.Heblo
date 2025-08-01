namespace Anela.Heblo.Application.Domain.Catalog.Price;

public class ProductPriceErp
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal PriceWithVat { get; set; }
    public decimal PurchasePriceWithVat { get; set; }
    public decimal PriceWithoutVat { get; set; }
    public decimal PurchasePrice { get; set; }
}