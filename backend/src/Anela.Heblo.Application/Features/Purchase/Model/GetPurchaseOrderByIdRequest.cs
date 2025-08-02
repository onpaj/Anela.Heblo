using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public record GetPurchaseOrderByIdRequest(Guid Id) : IRequest<GetPurchaseOrderByIdResponse>;