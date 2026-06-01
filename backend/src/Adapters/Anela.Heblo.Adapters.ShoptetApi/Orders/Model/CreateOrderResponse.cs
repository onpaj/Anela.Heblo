using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class CreateOrderResponse
{
    [JsonPropertyName("data")]
    public CreateOrderData Data { get; set; } = new();
}

public class CreateOrderData
{
    [JsonPropertyName("order")]
    public OrderSummary Order { get; set; } = new();
}
