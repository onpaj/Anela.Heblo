using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public class GetPurchaseOrderByIdResponse
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
    public List<PurchaseOrderHistoryDto> History { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

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
}

public class PurchaseOrderHistoryDto
{
    public int Id { get; set; }
    public string Action { get; set; } = null!;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime ChangedAt { get; set; }
    public string ChangedBy { get; set; } = null!;
}