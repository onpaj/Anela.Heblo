using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;

public class ResetOrderShipmentHandler : IRequestHandler<ResetOrderShipmentRequest, ResetOrderShipmentResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackingOrderClient _orderClient;
    private readonly ShipmentLabelsSettings _shipmentSettings;
    private readonly ILogger<ResetOrderShipmentHandler> _logger;

    public ResetOrderShipmentHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IOptions<ShipmentLabelsSettings> shipmentSettings,
        ILogger<ResetOrderShipmentHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _shipmentSettings = shipmentSettings.Value;
        _logger = logger;
    }

    public async Task<ResetOrderShipmentResponse> Handle(ResetOrderShipmentRequest request, CancellationToken ct)
    {
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

        var totalWeightGrams = order.Items.Sum(i => i.WeightGrams * i.Quantity);
        if (totalWeightGrams == 0)
            return new ResetOrderShipmentResponse(ErrorCodes.ShipmentOrderWeightUnavailable);

        var weightGrams = Math.Max(totalWeightGrams, _shipmentSettings.MinPackageWeightGrams);

        var options = await _shipmentClient.GetShippingOptionsAsync(request.OrderCode, ct);
        if (options.Count == 0)
            return new ResetOrderShipmentResponse(ErrorCodes.ShipmentCarrierNotResolved);

        var command = new CreateShipmentCommand
        {
            OrderCode = request.OrderCode,
            CarrierCode = options[0].CarrierCode,
            Package = new ShipmentPackage
            {
                WidthMm = _shipmentSettings.DefaultPackageWidthMm,
                HeightMm = _shipmentSettings.DefaultPackageHeightMm,
                DepthMm = _shipmentSettings.DefaultPackageDepthMm,
                WeightGrams = weightGrams,
            },
        };

        CreatedShipment createdShipment;
        try
        {
            createdShipment = await _shipmentClient.CreateShipmentAsync(command, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create replacement shipment for order {OrderCode}", request.OrderCode);
            return new ResetOrderShipmentResponse(ErrorCodes.ShipmentCreationFailed);
        }

        var newLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        var createdPackages = newLabels
            .Where(l => l.ShipmentGuid == createdShipment.ShipmentGuid)
            .ToList();
        var packages = createdPackages.Count > 0
            ? createdPackages.Select(l => new ResetShipmentPackage
            {
                Name = l.PackageName,
                LabelUrl = l.LabelUrl,
                LabelZpl = l.LabelZpl,
            }).ToList()
            : [new ResetShipmentPackage { Name = "PKG-1" }];

        return new ResetOrderShipmentResponse(new ResetShipmentData
        {
            ShipmentGuid = createdShipment.ShipmentGuid,
            Packages = packages,
        });
    }
}
