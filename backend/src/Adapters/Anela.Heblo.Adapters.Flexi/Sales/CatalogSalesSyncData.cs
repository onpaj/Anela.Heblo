using Anela.Heblo.Data;

namespace Anela.Heblo.Adapters.Flexi.Sales;

public class CatalogSalesSyncData : SyncData
{
    public CatalogSalesSyncData(IList<CatalogSalesFlexiDto> sales) : base("Product sales (Abra)", sales.Count, 0, 100)
    {
    }
}