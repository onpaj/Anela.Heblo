using Newtonsoft.Json;

namespace Anela.Heblo.Adapters.Flexi.Price;

public class ProductPriceFlexiDto
{
    [JsonProperty("idcenik")]
    public int ProductId { get; set; }

    [JsonProperty("kod")]
    public string ProductCode { get; set; }

    [JsonProperty("cena")]
    public decimal Price { get; set; }

    [JsonProperty("cenanakup")]
    public decimal PurchasePrice { get; set; }

    [JsonProperty("typszbdphk")]
    public string VatLevel { get; set; }

    [JsonProperty("typzasobyk")]
    public string ProductType { get; set; }

    [JsonProperty("idKusovnik")]
    public int? BoMId { get; set; }

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