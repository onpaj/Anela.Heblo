namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ProductStockSnapshot
{
    public decimal Eshop { get; init; }
    public decimal Erp { get; init; }
    public decimal Transport { get; init; }
    public decimal Manufactured { get; init; }
    public decimal Reserve { get; init; }
    public decimal Quarantine { get; init; }
    public decimal Ordered { get; init; }
    public decimal Planned { get; init; }
    public StockSourceKind PrimaryStockSource { get; init; } = StockSourceKind.Erp;

    public decimal WarehouseStock => PrimaryStockSource == StockSourceKind.Erp ? Erp : Eshop;

    public decimal Available => WarehouseStock + Transport + Manufactured;

    public decimal Total => Available + Reserve;
}
