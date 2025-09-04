using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class CatalogManufactureRecordDto
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("amount")]
    public double Amount { get; set; }

    [JsonPropertyName("pricePerPiece")]
    public decimal PricePerPiece { get; set; }

    [JsonPropertyName("priceTotal")]
    public decimal PriceTotal { get; set; }

    [JsonPropertyName("productCode")]
    public string ProductCode { get; set; } = string.Empty;

    [JsonPropertyName("documentNumber")]
    public string DocumentNumber { get; set; } = string.Empty;

    // Backward compatibility properties (derived from Date)
    [JsonPropertyName("year")]
    public int? Year => Date != DateTime.MinValue ? Date.Year : null;

    [JsonPropertyName("month")]
    public int? Month => Date != DateTime.MinValue ? Date.Month : null;
}