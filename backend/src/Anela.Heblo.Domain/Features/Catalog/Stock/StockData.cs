using Anela.Heblo.Domain.Features.Catalog.Lots;

namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public record StockData
{
    public decimal Eshop { get; set; }
    public decimal Erp { get; set; }
    public decimal Transport { get; set; }
    public decimal Manufactured { get; set; }
    public decimal Reserve { get; set; }
    public decimal Quarantine { get; set; }
    public decimal Ordered { get; set; }
    public decimal Planned { get; set; }

    /// <summary>
    /// Warehouse valuation per unit (Flexi <c>prumCena</c>, exact <c>tuz / stavMJ</c>) of the pieces
    /// currently in the item's own warehouse - what the stock actually cost, a weighted average over
    /// past batches. Differs from the ceník purchase price, which prices the next purchase or
    /// manufacture at today's prices. Null when the ERP (Flexi) warehouse holds nothing - gated on
    /// <see cref="Erp"/>, not <see cref="PrimaryStockSource"/>, so an e-shop-primary item can show
    /// available stock with no stock price.
    /// </summary>
    public decimal? StockPrice { get; set; }

    public StockSource PrimaryStockSource { get; set; } = StockSource.Erp;

    /// <summary>
    /// Stock physically present in the selling warehouse - the e-shop figure for products sold
    /// online, the ERP figure otherwise. Deliberately excludes goods still in transport and goods
    /// written down into the manufacture warehouse: neither can be picked from the warehouse yet.
    /// </summary>
    public decimal WarehouseStock => PrimaryStockSource == StockSource.Erp ? Erp : Eshop;

    public decimal Available => WarehouseStock + Transport + Manufactured;

    /// <summary>
    /// Total stock including both available stock and reserve stock
    /// </summary>
    public decimal Total => Available + Reserve; //  + Quarantine; // 2025-05-12 Quarantine should be out of all stock

    /// <summary>
    /// Effective stock including both available and ordered stock for purchase planning
    /// </summary>
    public decimal EffectiveStock => Available + Ordered;

    public List<CatalogLot> Lots { get; set; } = new();

}