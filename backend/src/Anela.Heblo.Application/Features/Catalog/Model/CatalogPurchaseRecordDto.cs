using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.Model;

public class CatalogPurchaseRecordDto
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("supplierName")]
    public string SupplierName { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public double Amount { get; set; }

    [JsonPropertyName("pricePerPiece")]
    public decimal PricePerPiece { get; set; }

    [JsonPropertyName("priceTotal")]
    public decimal PriceTotal { get; set; }

    [JsonPropertyName("documentNumber")]
    public string DocumentNumber { get; set; } = string.Empty;

    // Backward compatibility properties (derived from Date)
    [JsonPropertyName("year")]
    public int Year => Date.Year;

    [JsonPropertyName("month")]
    public int Month => Date.Month;
}