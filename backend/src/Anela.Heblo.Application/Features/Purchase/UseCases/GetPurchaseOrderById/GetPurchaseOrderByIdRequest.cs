using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderById;

public class GetPurchaseOrderByIdRequest : IRequest<GetPurchaseOrderByIdResponse>
{
    public int Id { get; set; }
}