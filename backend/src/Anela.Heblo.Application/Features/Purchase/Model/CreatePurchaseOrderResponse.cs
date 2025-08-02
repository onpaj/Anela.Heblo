namespace Anela.Heblo.Application.Features.Purchase.Model;

public record CreatePurchaseOrderResponse(
    Guid Id,
    string OrderNumber,
    Guid SupplierId,
    string SupplierName,
    DateTime OrderDate,
    DateTime? ExpectedDeliveryDate,
    string Status,
    string? Notes,
    decimal TotalAmount,
    List<PurchaseOrderLineDto> Lines,
    List<PurchaseOrderHistoryDto> History,
    DateTime CreatedAt,
    string CreatedBy,
    DateTime? UpdatedAt,
    string? UpdatedBy
);