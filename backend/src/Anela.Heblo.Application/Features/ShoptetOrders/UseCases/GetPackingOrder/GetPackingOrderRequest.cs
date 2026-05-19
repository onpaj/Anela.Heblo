using MediatR;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;

public class GetPackingOrderRequest : IRequest<GetPackingOrderResponse>
{
    public string Code { get; set; } = null!;
}
