using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class CatalogPurchaseRecordDto
{
    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("month")]
    public int Month { get; set; }

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
}