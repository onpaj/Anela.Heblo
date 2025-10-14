using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class MarginHistoryDto
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("sellingPrice")]
    public decimal SellingPrice { get; set; }

    [JsonPropertyName("totalCost")]
    public decimal TotalCost { get; set; }

    // M0-M3 margin levels
    [JsonPropertyName("m0")]
    public MarginLevelDto M0 { get; set; } = new();

    [JsonPropertyName("m1")]
    public MarginLevelDto M1 { get; set; } = new();

    [JsonPropertyName("m2")]
    public MarginLevelDto M2 { get; set; } = new();

    [JsonPropertyName("m3")]
    public MarginLevelDto M3 { get; set; } = new();
}