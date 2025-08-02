using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public record CreatePurchaseOrderRequest(
    Guid SupplierId,
    DateTime OrderDate,
    DateTime? ExpectedDeliveryDate,
    string? Notes,
    List<CreatePurchaseOrderLineRequest> Lines
) : IRequest<CreatePurchaseOrderResponse>;

public record CreatePurchaseOrderLineRequest(
    Guid MaterialId,
    decimal Quantity,
    decimal UnitPrice,
    string? Notes
);