using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public class GetPurchaseOrdersResponse
{
    public List<PurchaseOrderSummaryDto> Orders { get; set; } = null!;
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class PurchaseOrderSummaryDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = null!;
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public ContactVia? ContactVia { get; set; }
    public string Status { get; set; } = null!;
    public decimal TotalAmount { get; set; }
    public int LineCount { get; set; }
    public bool IsEditable { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
}