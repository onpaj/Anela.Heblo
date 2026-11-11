using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Products.Model;

public class EshopImageCutsResponse
{
    [JsonPropertyName("data")]
    public EshopImageCutsData? Data { get; set; }
}

public class EshopImageCutsData
{
    [JsonPropertyName("imageCuts")]
    public List<ImageCut> ImageCuts { get; set; } = new();
}

public class ImageCut
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("urlPath")]
    public string? UrlPath { get; set; }
}
