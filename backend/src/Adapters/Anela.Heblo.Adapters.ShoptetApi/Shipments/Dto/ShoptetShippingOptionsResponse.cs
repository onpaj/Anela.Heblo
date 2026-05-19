using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;

internal class ShoptetShippingOptionsResponse
{
    [JsonPropertyName("data")]
    public ShoptetShippingOptionsData? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<ShoptetErrorDto>? Errors { get; set; }
}

internal class ShoptetShippingOptionsData
{
    [JsonPropertyName("shippingOptions")]
    public List<ShoptetShippingOptionDto>? ShippingOptions { get; set; }
}

internal class ShoptetShippingOptionDto
{
    [JsonPropertyName("shippingId")]
    public int ShippingId { get; set; }

    [JsonPropertyName("methodName")]
    public string? MethodName { get; set; }

    [JsonPropertyName("carrierCode")]
    public string? CarrierCode { get; set; }
}
