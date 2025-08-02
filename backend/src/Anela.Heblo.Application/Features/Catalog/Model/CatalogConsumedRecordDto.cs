using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.Model;

public class CatalogConsumedRecordDto
{
    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("month")]
    public int Month { get; set; }

    [JsonPropertyName("amount")]
    public double Amount { get; set; }

    [JsonPropertyName("productName")]
    public string ProductName { get; set; } = string.Empty;
}