using Newtonsoft.Json;

namespace Anela.Heblo.Adapters.Flexi.Price;

public class ProductPriceFlexiDto
{
    [JsonProperty("idcenik")]
    public int ProductId { get; set; }

    [JsonProperty("kod")]
    public required string ProductCode { get; set; }

    [JsonProperty("cena")]
    public decimal Price { get; set; }

    [JsonProperty("cenanakup")]
    public decimal PurchasePrice { get; set; }

    [JsonProperty("typszbdphk")]
    public required string VatLevel { get; set; }

    [JsonProperty("typzasobyk")]
    public required string ProductType { get; set; }

    [JsonProperty("idKusovnik")]
    public int? BoMId { get; set; }

    /// <summary>
    /// Says which VAT semantics <see cref="Price"/> (<c>cenaZakl</c>) was entered under:
    /// "typCeny.bezDph" (excl-VAT) or "typCeny.sDph" (incl-VAT). User query 41 may not expose
    /// this field at all — it can come back null. Never assume it is excl-VAT without checking.
    /// </summary>
    [JsonProperty("typCenyDphK")]
    public string? TypCenyDphK { get; set; }

    /// <summary>True only when <see cref="TypCenyDphK"/> is explicitly "typCeny.sDph". A null
    /// value (the field is absent from query 41) is treated as excl-VAT by the caller, with a
    /// logged warning — never silently here.</summary>
    public bool IsPriceIncludingVat => TypCenyDphK == "typCeny.sDph";

    public decimal Vat
    {
        get
        {
            return VatLevel switch
            {
                "ovobozeno" => 0,
                "snížená" => 15,
                _ => 21
            };
        }
    }

    public bool HasCalculatedPurchasePrice => ProductType == "Výrobek";
    public bool HasBillOfMaterials => BoMId != null;
}