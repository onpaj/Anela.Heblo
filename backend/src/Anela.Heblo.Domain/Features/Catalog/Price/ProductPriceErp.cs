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

    /// <summary>
    /// Which VAT semantics Flexi's base price was entered under: "bezDph" (excl-VAT) or
    /// "sDph" (incl-VAT). Null means the ERP read did not expose it (user query 41 may not
    /// return <c>typCenyDphK</c>) — a real, reportable "unknown" state, not an error.
    /// </summary>
    public string? ErpPriceType { get; set; }
}