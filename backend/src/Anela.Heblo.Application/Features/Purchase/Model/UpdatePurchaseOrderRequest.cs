using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public record UpdatePurchaseOrderRequest(
    Guid Id,
    DateTime? ExpectedDeliveryDate,
    string? Notes,
    List<UpdatePurchaseOrderLineRequest> Lines
) : IRequest<UpdatePurchaseOrderResponse>;

public record UpdatePurchaseOrderLineRequest(
    Guid? Id,
    Guid MaterialId,
    decimal Quantity,
    decimal UnitPrice,
    string? Notes
);