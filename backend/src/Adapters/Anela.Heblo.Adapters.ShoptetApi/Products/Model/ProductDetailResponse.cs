using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Products.Model;

public class ProductDetailResponse
{
    [JsonPropertyName("data")]
    public ProductDetail? Data { get; set; }
}

public class ProductDetail
{
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    [JsonPropertyName("variants")]
    public List<ProductVariant> Variants { get; set; } = new();

    [JsonPropertyName("images")]
    public List<ProductImage>? Images { get; set; }
}

public class ProductVariant
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("visible")]
    public bool Visible { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    // Decimal strings use a dot ("0.035"); parse with InvariantCulture.
    [JsonPropertyName("weight")]
    public string? Weight { get; set; }

    [JsonPropertyName("height")]
    public string? Height { get; set; }

    [JsonPropertyName("width")]
    public string? Width { get; set; }

    [JsonPropertyName("depth")]
    public string? Depth { get; set; }

    [JsonPropertyName("atypicalShipping")]
    public bool AtypicalShipping { get; set; }
}

public class ProductImage
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("seoName")]
    public string? SeoName { get; set; }

    [JsonPropertyName("isMainImage")]
    public bool IsMainImage { get; set; }
}
