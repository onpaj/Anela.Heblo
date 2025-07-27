using Anela.Heblo.Data;

namespace Anela.Heblo.Adapters.Flexi.Purchase;

public class PurchaseHistorySyncData : SyncData
{
    public PurchaseHistorySyncData(IList<PurchaseHistory> purchaseHistory) : base("Purchase history (Abra)", purchaseHistory.Count, 0, 100)
    {
    }
}