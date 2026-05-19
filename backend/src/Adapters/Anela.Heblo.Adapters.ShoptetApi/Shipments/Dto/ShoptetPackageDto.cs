using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;

public class ShoptetPackageDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("labelUrl")]
    public string? LabelUrl { get; set; }

    [JsonPropertyName("labelZpl")]
    public string? LabelZpl { get; set; }

    [JsonPropertyName("trackingNumber")]
    public string? TrackingNumber { get; set; }

    [JsonPropertyName("trackingUrl")]
    public string? TrackingUrl { get; set; }
}
