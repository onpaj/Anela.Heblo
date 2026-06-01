using MediatR;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;

public class CreateOrderShipmentRequest : IRequest<CreateOrderShipmentResponse>
{
    public string OrderCode { get; set; } = null!;
    public bool ForceCreate { get; set; }
}
