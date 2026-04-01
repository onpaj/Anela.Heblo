using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class UpdateNotesRequest
{
    [JsonPropertyName("data")]
    public UpdateNotesData Data { get; set; } = new();
}

public class UpdateNotesData
{
    [JsonPropertyName("internalNote")]
    public string InternalNote { get; set; } = string.Empty;
}
