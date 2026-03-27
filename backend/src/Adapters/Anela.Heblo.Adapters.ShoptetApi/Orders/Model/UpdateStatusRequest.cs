using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class UpdateStatusRequest
{
    [JsonPropertyName("data")]
    public UpdateStatusData Data { get; set; } = new();
}

public class UpdateStatusData
{
    [JsonPropertyName("status")]
    public UpdateStatusValue Status { get; set; } = new();
}

public class UpdateStatusValue
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}
