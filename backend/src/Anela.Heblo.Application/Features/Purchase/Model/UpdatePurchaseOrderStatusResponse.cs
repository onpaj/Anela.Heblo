namespace Anela.Heblo.Application.Features.Purchase.Model;

public record UpdatePurchaseOrderStatusResponse(
    Guid Id,
    string OrderNumber,
    string Status,
    DateTime? UpdatedAt,
    string? UpdatedBy
);