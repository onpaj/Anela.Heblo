namespace Anela.Heblo.Application.Features.Purchase.Model;

public record GetPurchaseOrdersResponse(
    List<PurchaseOrderSummaryDto> Orders,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages
);

public record PurchaseOrderSummaryDto(
    Guid Id,
    string OrderNumber,
    Guid SupplierId,
    string SupplierName,
    DateTime OrderDate,
    DateTime? ExpectedDeliveryDate,
    string Status,
    decimal TotalAmount,
    int LineCount,
    DateTime CreatedAt,
    string CreatedBy
);