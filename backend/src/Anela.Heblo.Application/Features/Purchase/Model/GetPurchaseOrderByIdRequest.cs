using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public record GetPurchaseOrderByIdRequest(int Id) : IRequest<GetPurchaseOrderByIdResponse>;