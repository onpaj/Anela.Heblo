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
    [JsonPropertyName("m0Percentage")]
    public decimal M0Percentage { get; set; }

    [JsonPropertyName("m0Amount")]
    public decimal M0Amount { get; set; }

    [JsonPropertyName("m0CostBase")]
    public decimal M0CostBase { get; set; }

    [JsonPropertyName("m1Percentage")]
    public decimal M1Percentage { get; set; }

    [JsonPropertyName("m1Amount")]
    public decimal M1Amount { get; set; }

    [JsonPropertyName("m1CostBase")]
    public decimal M1CostBase { get; set; }

    [JsonPropertyName("m2Percentage")]
    public decimal M2Percentage { get; set; }

    [JsonPropertyName("m2Amount")]
    public decimal M2Amount { get; set; }

    [JsonPropertyName("m2CostBase")]
    public decimal M2CostBase { get; set; }

    [JsonPropertyName("m3Percentage")]
    public decimal M3Percentage { get; set; }

    [JsonPropertyName("m3Amount")]
    public decimal M3Amount { get; set; }

    [JsonPropertyName("m3CostBase")]
    public decimal M3CostBase { get; set; }
}