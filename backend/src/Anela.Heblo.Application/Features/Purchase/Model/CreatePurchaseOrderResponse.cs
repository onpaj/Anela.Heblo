namespace Anela.Heblo.Application.Features.Purchase.Model;

public record CreatePurchaseOrderResponse(
    int Id,
    string OrderNumber,
    int SupplierId,
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