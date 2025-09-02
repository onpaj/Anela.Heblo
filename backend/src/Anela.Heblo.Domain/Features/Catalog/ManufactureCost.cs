namespace Anela.Heblo.Domain.Features.Catalog;

public class ManufactureCost
{
    private static DateTime LaborRemovedFromBoMDate = new DateTime(2025, 8, 25); 
    
    public DateTime Date { get; set; }

    public decimal MaterialCost => Date > LaborRemovedFromBoMDate ? MaterialCostFromReceiptDocument : MaterialCostFromPurchasePrice;
    public decimal HandlingCost { get; set; }
    public decimal Total => MaterialCost + HandlingCost;
    
    public decimal MaterialCostFromReceiptDocument { get; set; }
    public decimal MaterialCostFromPurchasePrice { get; set; }
}