using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.Model;

public class ManufactureCostDto
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }
    
    [JsonPropertyName("materialCost")]
    public decimal MaterialCost { get; set; }
    
    [JsonPropertyName("handlingCost")]
    public decimal HandlingCost { get; set; }
    
    [JsonPropertyName("total")]
    public decimal Total { get; set; }
}