using Newtonsoft.Json;
using Volo.Abp.Domain.Entities;

namespace Anela.Heblo.Adapters.Flexi.Purchase;

public class PurchaseHistory : Entity<string>
{
    public override string Id 
    {
        get => ProductCode;
        protected set => ProductCode = value;
    }

    [JsonProperty("Nazev")]
    public string ProductName { get; set; }

    [JsonProperty("Kod")]
    public string ProductCode { get; set; }

    [JsonProperty("Datum")]
    public DateTime Date { get; set; }

    [JsonProperty("CisloDokladu")]
    public string PurchaseDocumentNo { get; set; }

    [JsonProperty("Mnozstvi")]
    public double Amount { get; set; }
    
    [JsonProperty("Sklad")]
    public int WarehouseId { get; set; }
    
    [JsonProperty("CenaMj")] 
    public decimal Price { get; set; }
    
    [JsonProperty("Firma")] 
    public string CompanyName { get; set; }
    
    [JsonProperty("FirmaId")] 
    public int? CompanyId { get; set; }
}