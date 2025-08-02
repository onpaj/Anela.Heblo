namespace Anela.Heblo.Application.Features.Purchase.Model;

public record CreatePurchaseOrderResponse(
    Guid Id,
    string OrderNumber,
    Guid SupplierId,
    DateTime OrderDate,
    DateTime? ExpectedDeliveryDate,
    string Status,
    string? Notes,
    decimal TotalAmount,
    List<PurchaseOrderLineDto> Lines,
    DateTime CreatedAt,
    string CreatedBy
);