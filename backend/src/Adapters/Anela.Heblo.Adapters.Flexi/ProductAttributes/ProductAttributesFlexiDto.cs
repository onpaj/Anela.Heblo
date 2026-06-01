using Newtonsoft.Json;

namespace Anela.Heblo.Adapters.Flexi.ProductAttributes;

public class ProductAttributesFlexiDto
{
    public const string ProductType_Product = "VYROBEK";
    public const string ProductType_Material = "MATERIÁL";

    [JsonProperty("cenikid")]
    public int ProductId { get; set; }

    [JsonProperty("cenikkod")]
    public required string ProductCode { get; set; }

    [JsonProperty("atributid")]
    public int AttributeId { get; set; }

    [JsonProperty("atributkod")]
    public required string AttributeCode { get; set; }

    [JsonProperty("hodnota")]
    public required string Value { get; set; }

    [JsonProperty("SkupinaZboziId")]
    public int ProductTypeId { get; set; }

    [JsonProperty("SkupinaZbozi")]
    public required string ProductType { get; set; }
}