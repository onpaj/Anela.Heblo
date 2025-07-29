using Anela.Heblo.Application.Domain.Catalog.Lots;

namespace Anela.Heblo.Application.Domain.Catalog.Stock;

public record StockData
{
    public decimal Eshop { get; set; }
    public decimal Erp { get; set; }
    public decimal Transport { get; set; }
    public decimal Reserve { get; set; }
    
    public StockSource PrimaryStockSource { get; set; } = StockSource.Erp;

    public decimal Available => (PrimaryStockSource == StockSource.Erp ? Erp : Eshop) + Transport;
    public List<CatalogLot> Lots { get; set; } = new ();

}