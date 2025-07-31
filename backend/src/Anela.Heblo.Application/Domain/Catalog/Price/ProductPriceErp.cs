namespace Anela.Heblo.Application.Domain.Catalog.Price;

public class ProductPriceErp
{
    public string ProductCode { get; set; }
    public decimal PriceWithVat { get; set; }
    public decimal PurchasePriceWithVat { get; set; }
    public decimal Price { get; set; }
    public decimal PurchasePrice { get; set; }
    public int? BoMId { get; set; }

    public bool HasBillOfMaterials => BoMId != null;
}