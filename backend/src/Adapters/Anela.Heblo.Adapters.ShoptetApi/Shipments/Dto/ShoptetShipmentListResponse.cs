using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;

internal class ShoptetShipmentListResponse
{
    [JsonPropertyName("data")]
    public ShoptetShipmentListData? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<ShoptetErrorDto>? Errors { get; set; }
}

internal class ShoptetShipmentListData
{
    [JsonPropertyName("items")]
    public List<ShoptetShipmentDto>? Items { get; set; }
}
