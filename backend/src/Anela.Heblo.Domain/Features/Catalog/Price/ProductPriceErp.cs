namespace Anela.Heblo.Domain.Features.Catalog.Price;

public class ProductPriceErp
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal PriceWithVat { get; set; }
    public decimal PurchasePriceWithVat { get; set; }
    public decimal PriceWithoutVat { get; set; }
    public decimal PurchasePrice { get; set; }

    public int? BoMId { get; set; }
    public bool HasBoM => BoMId != null;

    /// <summary>Internal ERP price list id (Flexi <c>idcenik</c>). 0 when unknown.</summary>
    public int ErpItemId { get; set; }
}