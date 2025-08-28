using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public class UpdatePurchaseOrderResponse
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public int SupplierId { get; set; } // Changed to int
    public string SupplierName { get; set; } = null!;
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public ContactVia? ContactVia { get; set; }
    public string Status { get; set; } = null!;
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public List<PurchaseOrderLineDto> Lines { get; set; } = null!;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}