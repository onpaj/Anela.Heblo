using Anela.Heblo.Catalog.Stock;
using Anela.Heblo.Data;

namespace Anela.Heblo.Adapters.Shoptet;

public class ProductShoptetStockSyncData : SyncData
{
    public ProductShoptetStockSyncData(IList<EshopStock> stockData) : base("Product stock (Shoptet)", stockData.Count, 0, 100)
    {
    }
}