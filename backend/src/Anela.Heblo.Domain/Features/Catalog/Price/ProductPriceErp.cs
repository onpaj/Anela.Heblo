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
    /// "sDph" (incl-VAT). Null means the ERP read did not expose it (a Flexi company whose
    /// copy of user query 41 does not select <c>typcenydphk</c>) — a real, reportable
    /// "unknown" state, not an error.
    /// </summary>
    public string? ErpPriceType { get; set; }

    /// <summary>
    /// The VAT rate the ERP's own VAT band positively identifies, or null when the band was
    /// not one the adapter recognises. Null is a real, reportable state — the write path
    /// refuses on it rather than assuming a rate, because an assumed rate turns straight
    /// into a wrong excl-VAT price in a live ERP with nothing anywhere reporting it.
    /// </summary>
    public decimal? VatRate { get; set; }
}