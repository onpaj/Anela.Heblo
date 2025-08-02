using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public record UpdatePurchaseOrderRequest(
    Guid Id,
    string SupplierName,
    DateTime? ExpectedDeliveryDate,
    string? Notes,
    List<UpdatePurchaseOrderLineRequest> Lines
) : IRequest<UpdatePurchaseOrderResponse>;

public record UpdatePurchaseOrderLineRequest(
    Guid? Id,
    string MaterialId, // Product code from catalog
    decimal Quantity,
    decimal UnitPrice,
    string? Notes
);