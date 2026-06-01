using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;

public class GetManufactureOrderRequest : IRequest<GetManufactureOrderResponse>
{
    public int Id { get; set; }
}