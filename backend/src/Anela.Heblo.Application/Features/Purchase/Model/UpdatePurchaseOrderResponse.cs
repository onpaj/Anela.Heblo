namespace Anela.Heblo.Application.Features.Purchase.Model;

public record UpdatePurchaseOrderResponse(
    Guid Id,
    string OrderNumber,
    Guid SupplierId, // Kept for backward compatibility, always Guid.Empty
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