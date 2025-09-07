using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.CreatePurchaseOrder;

public class CreatePurchaseOrderResponse : BaseResponse
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public long SupplierId { get; set; }
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

    public CreatePurchaseOrderResponse() : base() { }
    public CreatePurchaseOrderResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}