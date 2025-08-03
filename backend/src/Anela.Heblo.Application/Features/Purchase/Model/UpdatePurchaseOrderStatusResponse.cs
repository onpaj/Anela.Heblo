namespace Anela.Heblo.Application.Features.Purchase.Model;

public record UpdatePurchaseOrderStatusResponse(
    int Id,
    string OrderNumber,
    string Status,
    DateTime? UpdatedAt,
    string? UpdatedBy
);