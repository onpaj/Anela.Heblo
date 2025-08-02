namespace Anela.Heblo.Application.Features.Purchase.Model;

public record GetPurchaseOrderByIdResponse(
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
    List<PurchaseOrderHistoryDto> History,
    DateTime CreatedAt,
    string CreatedBy,
    DateTime? UpdatedAt,
    string? UpdatedBy
);

public record PurchaseOrderLineDto(
    Guid Id,
    Guid MaterialId,
    string MaterialName,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string? Notes
);

public record PurchaseOrderHistoryDto(
    Guid Id,
    string Action,
    string? OldValue,
    string? NewValue,
    DateTime ChangedAt,
    string ChangedBy
);