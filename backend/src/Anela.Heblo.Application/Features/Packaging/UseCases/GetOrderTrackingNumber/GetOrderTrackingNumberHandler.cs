using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Domain.Features.Packaging;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumber;

public class GetOrderTrackingNumberHandler
    : IRequestHandler<GetOrderTrackingNumberRequest, GetOrderTrackingNumberResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackageRepository _packageRepository;
    private readonly ILogger<GetOrderTrackingNumberHandler> _logger;

    public GetOrderTrackingNumberHandler(
        IShipmentClient shipmentClient,
        IPackageRepository packageRepository,
        ILogger<GetOrderTrackingNumberHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _packageRepository = packageRepository;
        _logger = logger;
    }

    public async Task<GetOrderTrackingNumberResponse> Handle(
        GetOrderTrackingNumberRequest request,
        CancellationToken cancellationToken)
    {
        string? trackingNumber;
        try
        {
            trackingNumber = await _shipmentClient.GetLatestActiveTrackingNumberAsync(request.OrderCode, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort: the confirmation screen falls back to the package name. Never surface an error here.
            _logger.LogWarning(ex,
                "GetOrderTrackingNumber: failed to fetch tracking for order {OrderCode}.", request.OrderCode);
            return new GetOrderTrackingNumberResponse { TrackingNumber = null };
        }

        if (!string.IsNullOrEmpty(trackingNumber))
        {
            await _packageRepository.SetTrackingNumberByOrderCodeAsync(request.OrderCode, trackingNumber, cancellationToken);
        }

        return new GetOrderTrackingNumberResponse { TrackingNumber = trackingNumber };
    }
}
