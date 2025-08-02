namespace Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;

public class CatalogPurchaseRecord
{
    public int? SupplierId { get; set; }
    public string SupplierName { get; set; }
    public DateTime Date { get; set; }
    public double Amount { get; set; }
    public decimal PricePerPiece { get; set; }
    public decimal PriceTotal { get; set; }
    public string ProductCode { get; set; }
    public string DocumentNumber { get; set; }
}