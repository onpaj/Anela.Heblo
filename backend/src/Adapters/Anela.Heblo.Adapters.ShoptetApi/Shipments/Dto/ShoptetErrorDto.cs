using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;

public class ShoptetErrorDto
{
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("instance")]
    public string? Instance { get; set; }
}
