namespace Anela.Heblo.Application.Domain.Catalog.PurchaseHistory;

public class PurchaseHistorySummary
{
    public Dictionary<string, MonthlyPurchaseSummary> MonthlyData { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}