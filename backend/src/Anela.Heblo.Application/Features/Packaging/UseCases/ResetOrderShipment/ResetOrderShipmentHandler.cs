using Anela.Heblo.Application.Features.Packaging.Services;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;

public class ResetOrderShipmentHandler : IRequestHandler<ResetOrderShipmentRequest, ResetOrderShipmentResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackingOrderClient _orderClient;
    private readonly IShipmentCreationService _shipmentCreationService;
    private readonly ILogger<ResetOrderShipmentHandler> _logger;

    public ResetOrderShipmentHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IShipmentCreationService shipmentCreationService,
        ILogger<ResetOrderShipmentHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _shipmentCreationService = shipmentCreationService;
        _logger = logger;
    }

    public async Task<ResetOrderShipmentResponse> Handle(ResetOrderShipmentRequest request, CancellationToken ct)
    {
        const int maxPackages = 10;
        if (request.NumberOfPackages < 1 || request.NumberOfPackages > maxPackages)
            return new ResetOrderShipmentResponse(ErrorCodes.InvalidPackageCount);

        var existingLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        if (existingLabels.Count == 0)
            return new ResetOrderShipmentResponse(ErrorCodes.NoShipmentToReset);

        var shipmentGuids = existingLabels
            .Select(l => l.ShipmentGuid)
            .Distinct()
            .ToList();

        foreach (var shipmentGuid in shipmentGuids)
        {
            try
            {
                await _shipmentClient.CancelShipmentAsync(shipmentGuid, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel shipment {ShipmentGuid} for order {OrderCode}",
                    shipmentGuid, request.OrderCode);
                return new ResetOrderShipmentResponse(ErrorCodes.ShipmentCancelFailed);
            }
        }

        var order = await _orderClient.GetPackingOrderAsync(request.OrderCode, ct);
        if (order is null)
            return new ResetOrderShipmentResponse(ErrorCodes.ShoptetOrderNotFound);

        // Reset never supplies an explicit packer today (ResetOrderShipmentRequest has no
        // PackingUserId field) — the shared service falls back to the current user's email.
        var result = await _shipmentCreationService.CreateAndPersistAsync(order, request.NumberOfPackages, null, ct);
        if (!result.IsSuccess)
            return new ResetOrderShipmentResponse(result.ErrorCode!.Value);

        var packages = result.Labels
            .Select(label => new ResetShipmentPackage
            {
                TrackingNumber = label.TrackingNumber,
                LabelUrl = label.LabelUrl,
                LabelZpl = label.LabelZpl,
            })
            .ToList();

        return new ResetOrderShipmentResponse(new ResetShipmentData
        {
            ShipmentGuid = result.ShipmentGuid,
            Packages = packages,
            PendingCompletion = request.NumberOfPackages >= 2,
        });
    }
}
