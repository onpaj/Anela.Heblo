namespace Anela.Heblo.Application.Domain.Catalog.Price;

public class ProductPriceEshop
{
    public string Code { get; set; }
    public string PairCode { get; set; }
    public string Name { get; set; }
    public decimal? Price { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? OriginalPrice { get; set; }
    public decimal? OriginalPurchasePrice { get; set; }
}