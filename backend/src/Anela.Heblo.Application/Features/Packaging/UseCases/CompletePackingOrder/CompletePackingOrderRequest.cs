using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.CompletePackingOrder;

public class CompletePackingOrderRequest : IRequest<CompletePackingOrderResponse>
{
    public string OrderCode { get; set; } = null!;
}
