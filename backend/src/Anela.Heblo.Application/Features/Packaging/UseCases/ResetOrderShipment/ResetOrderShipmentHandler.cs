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

        var totalWeightGrams = order.Items.Sum(i => i.WeightGrams * i.Quantity);
        if (totalWeightGrams == 0)
        {
            // Carriers reject a 0 kg package; fall back to a default package weight.
            _logger.LogWarning(
                "Order {OrderCode} has no known item weights; using fallback package weight {Fallback}g",
                request.OrderCode, _shipmentSettings.FallbackPackageWeightGrams);
            totalWeightGrams = _shipmentSettings.FallbackPackageWeightGrams;
        }

        var n = request.NumberOfPackages;
        var perPackageWeightGrams = Math.Max(totalWeightGrams / n, _shipmentSettings.MinPackageWeightGrams);

        var options = await _shipmentClient.GetShippingOptionsAsync(request.OrderCode, ct);
        if (options.Count == 0)
            return new ResetOrderShipmentResponse(ErrorCodes.ShipmentCarrierNotResolved);

        var command = new CreateShipmentCommand
        {
            OrderCode = request.OrderCode,
            CarrierCode = options[0].CarrierCode,
            PackageCount = n,
            Package = new ShipmentPackage
            {
                WidthMm = _shipmentSettings.DefaultPackageWidthMm,
                HeightMm = _shipmentSettings.DefaultPackageHeightMm,
                DepthMm = _shipmentSettings.DefaultPackageDepthMm,
                WeightGrams = perPackageWeightGrams,
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

        // Shoptet generates labels asynchronously, so the response may contain fewer labels than the
        // requested `n`. Always produce exactly `n` entries (mirroring ScanPackingOrderHandler) so the
        // FE shows the correct "X/N" counter; packages with no label yet get null tracking + URLs.
        var newLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        var createdPackages = newLabels
            .Where(l => l.ShipmentGuid == createdShipment.ShipmentGuid)
            .ToList();
        var packages = Enumerable.Range(1, n)
            .Select(i =>
            {
                var label = i <= createdPackages.Count ? createdPackages[i - 1] : null;
                return new ResetShipmentPackage
                {
                    TrackingNumber = label?.TrackingNumber,
                    LabelUrl = label?.LabelUrl,
                    LabelZpl = label?.LabelZpl,
                };
            })
            .ToList();

        return new ResetOrderShipmentResponse(new ResetShipmentData
        {
            ShipmentGuid = createdShipment.ShipmentGuid,
            Packages = packages,
            PendingCompletion = n >= 2,
        });
    }
}
