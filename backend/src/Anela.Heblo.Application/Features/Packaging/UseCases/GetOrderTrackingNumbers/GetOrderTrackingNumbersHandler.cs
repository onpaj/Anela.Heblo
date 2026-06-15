using Anela.Heblo.Application.Features.ShipmentLabels;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumbers;

public class GetOrderTrackingNumbersHandler
    : IRequestHandler<GetOrderTrackingNumbersRequest, GetOrderTrackingNumbersResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly ILogger<GetOrderTrackingNumbersHandler> _logger;

    public GetOrderTrackingNumbersHandler(
        IShipmentClient shipmentClient,
        ILogger<GetOrderTrackingNumbersHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _logger = logger;
    }

    public async Task<GetOrderTrackingNumbersResponse> Handle(
        GetOrderTrackingNumbersRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var labels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, cancellationToken);

            // GetLabelsByOrderCodeAsync already filters to active (non-cancelled) shipments and
            // returns packages oldest-shipment-first. Group by shipment and take the latest one
            // (guids are time-ordered, same rule as GetLatestActiveTrackingNumberAsync), then return
            // its packages' tracking numbers in order.
            var trackingNumbers = labels
                .GroupBy(l => l.ShipmentGuid)
                .LastOrDefault()?
                .Select(l => l.TrackingNumber)
                .Where(t => !string.IsNullOrEmpty(t))
                .Select(t => t!)
                .ToList() ?? [];

            return new GetOrderTrackingNumbersResponse { TrackingNumbers = trackingNumbers };
        }
        catch (Exception ex)
        {
            // Best-effort: the confirmation screen falls back to the packages' own tracking numbers.
            _logger.LogWarning(ex,
                "GetOrderTrackingNumbers: failed to fetch tracking for order {OrderCode}.", request.OrderCode);
            return new GetOrderTrackingNumbersResponse { TrackingNumbers = [] };
        }
    }
}
