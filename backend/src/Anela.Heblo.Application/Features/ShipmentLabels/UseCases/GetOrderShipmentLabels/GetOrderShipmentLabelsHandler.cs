using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
using Anela.Heblo.Application.Shared;
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

    public async Task<GetOrderShipmentLabelsResponse> Handle(
        GetOrderShipmentLabelsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var labels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, cancellationToken);

            if (labels.Count == 0)
            {
                return new GetOrderShipmentLabelsResponse(
                    ErrorCodes.ShipmentLabelsNoShipmentFound,
                    new Dictionary<string, string> { { "orderCode", request.OrderCode } });
            }

            if (labels.All(l => l.LabelUrl is null && l.LabelZpl is null))
            {
                return new GetOrderShipmentLabelsResponse(
                    ErrorCodes.ShipmentLabelsNotGenerated,
                    new Dictionary<string, string> { { "orderCode", request.OrderCode } });
            }

            var dtos = labels.Select(l => new ShipmentLabelDto
            {
                ShipmentGuid = l.ShipmentGuid,
                PackageName = l.PackageName,
                LabelUrl = l.LabelUrl,
                LabelZpl = l.LabelZpl,
                TrackingNumber = l.TrackingNumber,
                TrackingUrl = l.TrackingUrl,
            }).ToList();

            return new GetOrderShipmentLabelsResponse(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get shipment labels for order {OrderCode}", request.OrderCode);
            return new GetOrderShipmentLabelsResponse(ErrorCodes.InternalServerError);
        }
    }
}
