namespace Anela.Heblo.Application.Features.Purchase.Model;

public record UpdatePurchaseOrderResponse(
    int Id,
    string OrderNumber,
    int SupplierId, // Changed to int
    string SupplierName,
    DateTime OrderDate,
    DateTime? ExpectedDeliveryDate,
    string Status,
    string? Notes,
    decimal TotalAmount,
    List<PurchaseOrderLineDto> Lines,
    DateTime? UpdatedAt,
    string? UpdatedBy
);