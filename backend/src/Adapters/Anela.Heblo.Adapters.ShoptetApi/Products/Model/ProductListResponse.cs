using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Products.Model;

public class ProductListResponse
{
    [JsonPropertyName("data")]
    public ProductListData? Data { get; set; }
}

public class ProductListData
{
    [JsonPropertyName("products")]
    public List<ProductListItem> Products { get; set; } = new();

    [JsonPropertyName("paginator")]
    public Paginator? Paginator { get; set; }
}

public class ProductListItem
{
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }
}

public class Paginator
{
    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }
}
