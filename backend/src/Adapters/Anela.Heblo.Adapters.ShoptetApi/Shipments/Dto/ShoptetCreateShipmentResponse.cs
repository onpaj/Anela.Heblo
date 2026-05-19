using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;

internal class ShoptetCreateShipmentResponse
{
    [JsonPropertyName("data")]
    public ShoptetCreateShipmentData? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<ShoptetErrorDto>? Errors { get; set; }
}

internal class ShoptetCreateShipmentData
{
    [JsonPropertyName("guid")]
    public Guid Guid { get; set; }

    [JsonPropertyName("checkUrls")]
    public object? CheckUrls { get; set; }
}
