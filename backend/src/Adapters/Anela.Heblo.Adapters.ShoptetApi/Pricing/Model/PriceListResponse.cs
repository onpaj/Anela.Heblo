using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Pricing.Model;

public class PriceListResponse
{
    [JsonPropertyName("data")]
    public PriceListResponseData? Data { get; set; }
}

public class PriceListResponseData
{
    [JsonPropertyName("pricelists")]
    public List<PriceListSummary> PriceLists { get; set; } = new();
}

public class PriceListSummary
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("default")]
    public bool IsDefault { get; set; }
}
