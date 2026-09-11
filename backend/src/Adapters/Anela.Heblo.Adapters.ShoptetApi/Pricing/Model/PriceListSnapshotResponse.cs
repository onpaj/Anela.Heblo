using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Pricing.Model;

public class PriceListSnapshotResponse
{
    [JsonPropertyName("data")]
    public PriceListSnapshotData? Data { get; set; }
}

public class PriceListSnapshotData
{
    [JsonPropertyName("pricelist")]
    public List<PriceListSnapshotItem> Items { get; set; } = new();

    [JsonPropertyName("paginator")]
    public PriceListPaginator? Paginator { get; set; }
}

public class PriceListSnapshotItem
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>Shoptet returns prices as strings with 2 decimals, e.g. "190.00".</summary>
    [JsonPropertyName("priceWithVat")]
    public string? PriceWithVat { get; set; }
}

public class PriceListPaginator
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }
}
