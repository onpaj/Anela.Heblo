namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrderStatus;

public record UpdatePurchaseOrderStatusResponse(
    int Id,
    string OrderNumber,
    string Status,
    DateTime? UpdatedAt,
    string? UpdatedBy
);