using Newtonsoft.Json;

namespace Anela.Heblo.Adapters.Flexi.SemiProducts;

public class SemiProductFlexiDto
{
    [JsonProperty("kod")]
    public string ProductCode { get; set; }
    [JsonProperty("nazev")]
    public string ProductName { get; set; }
}