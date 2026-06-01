using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class CreateOrderRemarkRequest
{
    [JsonPropertyName("data")]
    public CreateOrderRemarkData Data { get; set; } = new();
}

public class CreateOrderRemarkData
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "system";
}
