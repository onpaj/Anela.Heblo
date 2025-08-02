namespace Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;

public class SupplierPurchaseSummary
{
    public string SupplierName { get; set; }
    public double Amount { get; set; }
    public decimal Cost { get; set; }
    public int PurchaseCount { get; set; }
}