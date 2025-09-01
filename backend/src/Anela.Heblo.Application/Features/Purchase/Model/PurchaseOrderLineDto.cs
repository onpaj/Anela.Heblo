namespace Anela.Heblo.Application.Features.Purchase.Model;

public class PurchaseOrderLineDto
{
    public int Id { get; set; }
    public string MaterialId { get; set; } = null!;
    public string Code { get; set; } = null!; // Same as MaterialId for compatibility
    public string MaterialName { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string? Notes { get; set; }
    public string? CatalogNote { get; set; } // Note from catalog item
}