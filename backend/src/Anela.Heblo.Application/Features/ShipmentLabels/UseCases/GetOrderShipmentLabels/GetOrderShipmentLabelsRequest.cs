using MediatR;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;

public class GetOrderShipmentLabelsRequest : IRequest<GetOrderShipmentLabelsResponse>
{
    public string OrderCode { get; set; } = null!;
}
