using System.Text.Json.Serialization;
using Anela.Heblo.Adapters.ShoptetApi.Products.Model;

namespace Anela.Heblo.Adapters.ShoptetApi.Stock.Model;

public class GetSuppliesResponse
{
    [JsonPropertyName("data")]
    public GetSuppliesData? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<UpdateStockError>? Errors { get; set; }
}

public class GetSuppliesData
{
    [JsonPropertyName("supplies")]
    public List<SupplyItem> Supplies { get; set; } = new();

    [JsonPropertyName("paginator")]
    public Paginator? Paginator { get; set; }
}

public class SupplyItem
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("claim")]
    public string? Claim { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("productGuid")]
    public string? ProductGuid { get; set; }
}
