using Newtonsoft.Json;

namespace Anela.Heblo.Adapters.Flexi.Materials;

public class ConsumedMaterialsFlexiDto
{
    [JsonProperty("kod")]
    public string ProductCode { get; set; }
    
    [JsonProperty("nazev")]
    public string ProductName { get; set; }

    [JsonProperty("mnozmj")]
    public double Amount { get; set; }

    [JsonProperty("vydejkadatum")]
    public string Date { get; set; }
}