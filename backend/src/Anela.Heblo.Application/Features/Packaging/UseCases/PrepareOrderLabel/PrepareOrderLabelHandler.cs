using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.PrepareOrderLabel;

public class PrepareOrderLabelHandler
    : IRequestHandler<PrepareOrderLabelRequest, PrepareOrderLabelResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackingOrderClient _orderClient;
    private readonly ShipmentLabelsSettings _shipmentSettings;
    private readonly ShoptetOrdersSettings _orderSettings;
    private readonly ILogger<PrepareOrderLabelHandler> _logger;

    public PrepareOrderLabelHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IOptions<ShipmentLabelsSettings> shipmentSettings,
        IOptions<ShoptetOrdersSettings> orderSettings,
        ILogger<PrepareOrderLabelHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _shipmentSettings = shipmentSettings.Value;
        _orderSettings = orderSettings.Value;
        _logger = logger;
    }

    public async Task<PrepareOrderLabelResponse> Handle(
        PrepareOrderLabelRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Load order — null means the order doesn't exist in Shoptet
            var order = await _orderClient.GetPackingOrderAsync(request.OrderCode, cancellationToken);
            if (order is null)
                return new PrepareOrderLabelResponse(ErrorCodes.ShoptetOrderNotFound);

            // 2. Order must be in the packing state
            if (order.StatusId != _orderSettings.PackingStateId)
                return new PrepareOrderLabelResponse(ErrorCodes.OrderNotInPackingState);

            // 3. Return existing labels when force-recreate is not requested
            var existingLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(
                request.OrderCode, cancellationToken);

            if (existingLabels.Count > 0 && !request.ForceRecreate)
                return new PrepareOrderLabelResponse(MapToDtos(existingLabels));

            // 4. Compute total weight — zero weight means product data is incomplete
            var totalWeightGrams = order.Items.Sum(i => i.WeightGrams * i.Quantity);
            if (totalWeightGrams == 0)
                return new PrepareOrderLabelResponse(ErrorCodes.ShipmentOrderWeightUnavailable);

            var packageWeightGrams = Math.Max(totalWeightGrams, _shipmentSettings.MinPackageWeightGrams);

            // 5. Resolve carrier from Balíkobot
            var shippingOptions = await _shipmentClient.GetShippingOptionsAsync(
                request.OrderCode, cancellationToken);

            if (shippingOptions.Count == 0)
                return new PrepareOrderLabelResponse(ErrorCodes.ShipmentCarrierNotResolved);

            // 6. Create the shipment
            var command = new CreateShipmentCommand
            {
                OrderCode = request.OrderCode,
                CarrierCode = shippingOptions[0].CarrierCode,
                Package = new ShipmentPackage
                {
                    WidthMm = _shipmentSettings.DefaultPackageWidthMm,
                    HeightMm = _shipmentSettings.DefaultPackageHeightMm,
                    DepthMm = _shipmentSettings.DefaultPackageDepthMm,
                    WeightGrams = packageWeightGrams,
                },
            };

            try
            {
                await _shipmentClient.CreateShipmentAsync(command, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Shipment API failed to create shipment for order {OrderCode}", request.OrderCode);
                return new PrepareOrderLabelResponse(ErrorCodes.ShipmentCreationFailed);
            }

            // 7. Poll for label URL — one retry after 3-second delay
            var labels = await _shipmentClient.GetLabelsByOrderCodeAsync(
                request.OrderCode, cancellationToken);
            var labelReady = labels.Any(l => l.LabelUrl is not null);

            if (!labelReady)
            {
                await Task.Delay(3000, cancellationToken);
                labels = await _shipmentClient.GetLabelsByOrderCodeAsync(
                    request.OrderCode, cancellationToken);
                labelReady = labels.Any(l => l.LabelUrl is not null);
            }

            return new PrepareOrderLabelResponse(labelReady, MapToDtos(labels));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error preparing label for order {OrderCode}", request.OrderCode);
            return new PrepareOrderLabelResponse(ErrorCodes.InternalServerError);
        }
    }

    private static IReadOnlyList<ShipmentLabelDto> MapToDtos(IReadOnlyList<ShipmentLabel> labels) =>
        labels.Select(l => new ShipmentLabelDto
        {
            ShipmentGuid = l.ShipmentGuid,
            PackageName = l.PackageName,
            LabelUrl = l.LabelUrl,
            LabelZpl = l.LabelZpl,
            TrackingNumber = l.TrackingNumber,
            TrackingUrl = l.TrackingUrl,
        }).ToList();
}
