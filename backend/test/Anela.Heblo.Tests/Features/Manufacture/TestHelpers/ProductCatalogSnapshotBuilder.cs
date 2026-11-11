using Anela.Heblo.Application.Features.Manufacture.Contracts;

namespace Anela.Heblo.Tests.Features.Manufacture.TestHelpers;

/// <summary>
/// Fluent builder for ProductCatalogSnapshot. Defaults mirror a freshly constructed CatalogAggregate.
/// </summary>
public class ProductCatalogSnapshotBuilder
{
    private string _productCode = "TEST001";
    private string _productName = "";
    private string _productNameNormalized = "";
    private ProductKind _type = ProductKind.UNDEFINED;
    private double _minimalManufactureQuantity;
    private double? _netWeight;
    private bool _hasLots;
    private double? _manufactureDifficulty;
    private IReadOnlyList<ProductManufactureRecordSnapshot> _manufactured = [];
    private ProductStockSnapshot _stock = new();
    private ProductPropertiesSnapshot _properties = new();
    private IReadOnlyList<ProductSalesRecordSnapshot> _sales = [];
    private IReadOnlyList<ProductConsumptionRecordSnapshot> _consumed = [];
    private IReadOnlyList<ProductStockTakingSnapshot> _stockTakings = [];

    public ProductCatalogSnapshotBuilder WithProductCode(string code) { _productCode = code; return this; }

    public ProductCatalogSnapshotBuilder WithProductName(string name, string? normalized = null)
    {
        _productName = name;
        _productNameNormalized = normalized ?? name;
        return this;
    }

    public ProductCatalogSnapshotBuilder WithType(ProductKind type) { _type = type; return this; }
    public ProductCatalogSnapshotBuilder WithMinimalManufactureQuantity(double value) { _minimalManufactureQuantity = value; return this; }
    public ProductCatalogSnapshotBuilder WithNetWeight(double? value) { _netWeight = value; return this; }
    public ProductCatalogSnapshotBuilder WithHasLots(bool value) { _hasLots = value; return this; }
    public ProductCatalogSnapshotBuilder WithManufactureDifficulty(double? value) { _manufactureDifficulty = value; return this; }
    public ProductCatalogSnapshotBuilder WithManufactureHistory(params ProductManufactureRecordSnapshot[] records) { _manufactured = records; return this; }
    public ProductCatalogSnapshotBuilder WithStock(ProductStockSnapshot stock) { _stock = stock; return this; }
    public ProductCatalogSnapshotBuilder WithProperties(ProductPropertiesSnapshot properties) { _properties = properties; return this; }
    public ProductCatalogSnapshotBuilder WithSalesHistory(params ProductSalesRecordSnapshot[] records) { _sales = records; return this; }
    public ProductCatalogSnapshotBuilder WithConsumedHistory(params ProductConsumptionRecordSnapshot[] records) { _consumed = records; return this; }
    public ProductCatalogSnapshotBuilder WithStockTakingHistory(params ProductStockTakingSnapshot[] records) { _stockTakings = records; return this; }

    public ProductCatalogSnapshot Build() => new()
    {
        ProductCode = _productCode,
        ProductName = _productName,
        ProductNameNormalized = _productNameNormalized,
        Type = _type,
        MinimalManufactureQuantity = _minimalManufactureQuantity,
        NetWeight = _netWeight,
        HasLots = _hasLots,
        ManufactureDifficulty = _manufactureDifficulty,
        ManufactureHistory = _manufactured,
        Stock = _stock,
        Properties = _properties,
        SalesHistory = _sales,
        ConsumedHistory = _consumed,
        StockTakingHistory = _stockTakings,
    };
}
