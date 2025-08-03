using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public record UpdatePurchaseOrderRequest(
    int Id,
    string SupplierName,
    DateTime? ExpectedDeliveryDate,
    string? Notes,
    List<UpdatePurchaseOrderLineRequest> Lines
) : IRequest<UpdatePurchaseOrderResponse>;

public record UpdatePurchaseOrderLineRequest(
    int? Id,
    string MaterialId, // Product code from catalog
    string? Name, // Optional - will use ProductName from catalog if available
    decimal Quantity,
    decimal UnitPrice,
    string? Notes
);