using System.Text.Json.Serialization;

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
}

public class SupplyItem
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("claim")]
    public string? Claim { get; set; }
}
