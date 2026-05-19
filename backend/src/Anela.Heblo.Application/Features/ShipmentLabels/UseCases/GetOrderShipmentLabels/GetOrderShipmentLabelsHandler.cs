using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;

public class GetOrderShipmentLabelsHandler : IRequestHandler<GetOrderShipmentLabelsRequest, GetOrderShipmentLabelsResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly ILogger<GetOrderShipmentLabelsHandler> _logger;

    public GetOrderShipmentLabelsHandler(
        IShipmentClient shipmentClient,
        ILogger<GetOrderShipmentLabelsHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _logger = logger;
    }

    public Task<GetOrderShipmentLabelsResponse> Handle(
        GetOrderShipmentLabelsRequest request,
        CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
