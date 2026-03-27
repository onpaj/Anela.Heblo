using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class UpdateStatusRequest
{
    [JsonPropertyName("data")]
    public UpdateStatusData Data { get; set; } = new();
}

public class UpdateStatusData
{
    // Flat integer, NOT a nested {status:{id:x}} — verified against Shoptet OpenAPI spec.
    [JsonPropertyName("statusId")]
    public int StatusId { get; set; }
}
