using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class IngredientDto
{
    [JsonPropertyName("productCode")]
    public string ProductCode { get; set; }

    [JsonPropertyName("productName")]
    public string ProductName { get; set; }

    [JsonPropertyName("amount")]
    public double Amount { get; set; }

    [JsonPropertyName("unit")]
    public string Unit { get; set; }
}
