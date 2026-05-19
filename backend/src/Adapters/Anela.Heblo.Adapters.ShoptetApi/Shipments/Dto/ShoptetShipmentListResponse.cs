using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;

public class ShoptetShipmentListResponse
{
    [JsonPropertyName("data")]
    public ShoptetShipmentListData? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<ShoptetErrorDto>? Errors { get; set; }
}

public class ShoptetShipmentListData
{
    [JsonPropertyName("items")]
    public List<ShoptetShipmentDto>? Items { get; set; }
}
