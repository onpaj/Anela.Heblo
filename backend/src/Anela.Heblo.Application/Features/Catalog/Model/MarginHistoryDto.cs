using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.Model;

public class MarginHistoryDto
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("marginAmount")]
    public decimal MarginAmount { get; set; }

    [JsonPropertyName("marginPercentage")]
    public decimal MarginPercentage { get; set; }

    [JsonPropertyName("sellingPrice")]
    public decimal SellingPrice { get; set; }

    [JsonPropertyName("totalCost")]
    public decimal TotalCost { get; set; }
}