using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;

internal class ShoptetCreateShipmentRequestEnvelope
{
    [JsonPropertyName("data")]
    public ShoptetCreateShipmentRequestData Data { get; set; } = null!;
}

internal class ShoptetCreateShipmentRequestData
{
    [JsonPropertyName("orderCode")]
    public string OrderCode { get; set; } = null!;

    [JsonPropertyName("shippingId")]
    public int ShippingId { get; set; }

    [JsonPropertyName("packages")]
    public List<ShoptetCreatePackageDto> Packages { get; set; } = [];
}

internal class ShoptetCreatePackageDto
{
    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("depth")]
    public int Depth { get; set; }

    // Weight in kg as string decimal, e.g. "0.500" (Shoptet requires string, not float)
    [JsonPropertyName("weight")]
    public string Weight { get; set; } = null!;
}
