using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public record CreatePurchaseOrderRequest(
    string SupplierName,
    string OrderDate,
    string? ExpectedDeliveryDate,
    string? Notes,
    List<CreatePurchaseOrderLineRequest>? Lines = null
) : IRequest<CreatePurchaseOrderResponse>;

public record CreatePurchaseOrderLineRequest(
    string MaterialId,
    string? Name, // Optional - will use ProductName from catalog if available
    decimal Quantity,
    decimal UnitPrice,
    string? Notes
);