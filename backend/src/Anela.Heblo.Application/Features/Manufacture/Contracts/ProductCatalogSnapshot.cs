namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

/// <summary>
/// Manufacture-owned read projection of a Catalog product. Produced by the Catalog module's
/// CatalogAggregateSnapshotMapper; carries only what Manufacture consumes.
/// </summary>
public class ProductCatalogSnapshot
{
    public required string ProductCode { get; init; }
    public string ProductName { get; init; } = "";
    public string ProductNameNormalized { get; init; } = "";
    public ProductKind Type { get; init; }
    public double MinimalManufactureQuantity { get; init; }
    public double? NetWeight { get; init; }
    public bool HasLots { get; init; }
    public ProductStockSnapshot Stock { get; init; } = new();
    public ProductPropertiesSnapshot Properties { get; init; } = new();
    public IReadOnlyList<ProductSalesRecordSnapshot> SalesHistory { get; init; } = [];
    public IReadOnlyList<ProductConsumptionRecordSnapshot> ConsumedHistory { get; init; } = [];
    public IReadOnlyList<ProductStockTakingSnapshot> StockTakingHistory { get; init; } = [];

    public string? ProductFamily => ProductCode?.Substring(0, Math.Min(6, ProductCode.Length));

    public string? SizeCode => ProductCode?.Substring(Math.Min(6, ProductCode.Length));

    public DateTime? LastStockTaking => StockTakingHistory.Count == 0 ? null : StockTakingHistory.Max(r => r.Date);
}
