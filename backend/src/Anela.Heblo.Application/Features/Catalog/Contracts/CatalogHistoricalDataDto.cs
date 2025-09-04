using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class CatalogHistoricalDataDto
{
    [JsonPropertyName("salesHistory")]
    public List<CatalogSalesRecordDto> SalesHistory { get; set; } = new();

    [JsonPropertyName("purchaseHistory")]
    public List<CatalogPurchaseRecordDto> PurchaseHistory { get; set; } = new();

    [JsonPropertyName("consumedHistory")]
    public List<CatalogConsumedRecordDto> ConsumedHistory { get; set; } = new();

    [JsonPropertyName("manufactureHistory")]
    public List<CatalogManufactureRecordDto> ManufactureHistory { get; set; } = new();

    [JsonPropertyName("manufactureCostHistory")]
    public List<ManufactureCostDto> ManufactureCostHistory { get; set; } = new();

    [JsonPropertyName("marginHistory")]
    public List<MarginHistoryDto> MarginHistory { get; set; } = new();
}