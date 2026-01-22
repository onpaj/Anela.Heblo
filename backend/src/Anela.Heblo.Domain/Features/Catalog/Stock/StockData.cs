using Anela.Heblo.Domain.Features.Catalog.Lots;

namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public record StockData
{
    public decimal Eshop { get; set; }
    public decimal Erp { get; set; }
    public decimal Transport { get; set; }
    public decimal Reserve { get; set; }
    public decimal Ordered { get; set; }
    public decimal Planned { get; set; }

    public StockSource PrimaryStockSource { get; set; } = StockSource.Erp;

    public decimal Available => (PrimaryStockSource == StockSource.Erp ? Erp : Eshop) + Transport;

    /// <summary>
    /// Total stock including both available stock and reserve stock
    /// </summary>
    public decimal Total => Available + Reserve;

    /// <summary>
    /// Effective stock including both available and ordered stock for purchase planning
    /// </summary>
    public decimal EffectiveStock => Available + Ordered;

    public List<CatalogLot> Lots { get; set; } = new();

}