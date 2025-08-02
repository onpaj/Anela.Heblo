namespace Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;

public class MonthlyPurchaseSummary
{
    public int Year { get; set; }
    public int Month { get; set; }
    public double TotalAmount { get; set; }
    public decimal TotalCost { get; set; }
    public decimal AveragePricePerPiece { get; set; }
    public int PurchaseCount { get; set; }
    public Dictionary<string, SupplierPurchaseSummary> SupplierBreakdown { get; set; } = new();

    public string MonthKey => $"{Year:D4}-{Month:D2}";
}