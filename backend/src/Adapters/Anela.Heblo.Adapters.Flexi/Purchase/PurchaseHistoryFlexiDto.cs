using Anela.Heblo.Adapters.Flexi.Common;
using Newtonsoft.Json;

namespace Anela.Heblo.Adapters.Flexi.Purchase;

public class PurchaseHistoryFlexiDto
{
    [JsonProperty("Nazev")]
    public required string ProductName { get; set; }

    [JsonProperty("Kod")]
    public required string ProductCode { get; set; }

    [JsonProperty("Datum")]
    [JsonConverter(typeof(UnspecifiedDateTimeConverter))]
    public DateTime Date { get; set; }

    [JsonProperty("CisloDokladu")]
    public required string PurchaseDocumentNo { get; set; }

    [JsonProperty("Mnozstvi")]
    public double Amount { get; set; }

    [JsonProperty("Sklad")]
    public int WarehouseId { get; set; }

    [JsonProperty("CenaMj")]
    public decimal Price { get; set; }

    [JsonProperty("Firma")]
    public required string CompanyName { get; set; }

    [JsonProperty("FirmaId")]
    public int? CompanyId { get; set; }
}