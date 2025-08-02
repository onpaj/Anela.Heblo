using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public record UpdatePurchaseOrderStatusRequest(
    Guid Id,
    string Status
) : IRequest<UpdatePurchaseOrderStatusResponse>;