using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrderStatus;

public record UpdatePurchaseOrderStatusRequest(
    int Id,
    string Status
) : IRequest<UpdatePurchaseOrderStatusResponse>;