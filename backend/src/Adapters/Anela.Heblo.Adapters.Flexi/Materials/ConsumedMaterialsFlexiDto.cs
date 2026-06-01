using Newtonsoft.Json;

namespace Anela.Heblo.Adapters.Flexi.Materials;

public class ConsumedMaterialsFlexiDto
{
    [JsonProperty("kod")]
    public required string ProductCode { get; set; }

    [JsonProperty("nazev")]
    public required string ProductName { get; set; }

    [JsonProperty("mnozmj")]
    public double Amount { get; set; }

    [JsonProperty("vydejkadatum")]
    public required string Date { get; set; }
}