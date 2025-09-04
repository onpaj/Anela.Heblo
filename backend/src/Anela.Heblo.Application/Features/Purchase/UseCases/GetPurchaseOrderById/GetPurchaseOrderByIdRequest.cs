using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderById;

public record GetPurchaseOrderByIdRequest(int Id) : IRequest<GetPurchaseOrderByIdResponse>;