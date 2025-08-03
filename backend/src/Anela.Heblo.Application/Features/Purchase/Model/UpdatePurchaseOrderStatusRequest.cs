using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public record UpdatePurchaseOrderStatusRequest(
    int Id,
    string Status
) : IRequest<UpdatePurchaseOrderStatusResponse>;