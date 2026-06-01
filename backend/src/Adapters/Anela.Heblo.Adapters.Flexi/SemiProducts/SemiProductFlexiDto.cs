using Newtonsoft.Json;

namespace Anela.Heblo.Adapters.Flexi.SemiProducts;

public class SemiProductFlexiDto
{
    [JsonProperty("kod")]
    public required string ProductCode { get; set; }
    [JsonProperty("nazev")]
    public required string ProductName { get; set; }
}