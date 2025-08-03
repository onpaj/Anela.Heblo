namespace Anela.Heblo.Application.Features.Purchase.Model;

public record GetPurchaseOrderByIdResponse(
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
    List<PurchaseOrderHistoryDto> History,
    DateTime CreatedAt,
    string CreatedBy,
    DateTime? UpdatedAt,
    string? UpdatedBy
);

public record PurchaseOrderLineDto(
    int Id,
    string MaterialId,
    string Code, // Same as MaterialId for compatibility
    string MaterialName,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string? Notes
);

public record PurchaseOrderHistoryDto(
    int Id,
    string Action,
    string? OldValue,
    string? NewValue,
    DateTime ChangedAt,
    string ChangedBy
);