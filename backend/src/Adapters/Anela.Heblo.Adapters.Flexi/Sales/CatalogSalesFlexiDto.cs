using Anela.Heblo.Adapters.Flexi.Common;
using Newtonsoft.Json;

namespace Anela.Heblo.Adapters.Flexi.Sales;

public class CatalogSalesFlexiDto
{
    [JsonProperty("datum")]
    [JsonConverter(typeof(UnspecifiedDateTimeConverter))]
    public DateTime Date { get; set; }

    [JsonProperty("produktkod")]
    public string ProductCode { get; set; }

    [JsonProperty("nazevproduktu")]
    public string ProductName { get; set; }

    [JsonProperty("mnozstvi")]
    public double AmountTotal { get; set; }
    [JsonProperty("mnozstvivo")]
    public double AmountB2B { get; set; }
    [JsonProperty("mnozstvimo")]
    public double AmountB2C { get; set; }

    [JsonProperty("suma")]
    public decimal SumTotal { get; set; }
    [JsonProperty("sumavo")]
    public decimal SumB2B { get; set; }
    [JsonProperty("sumamo")]
    public decimal SumB2C { get; set; }
}