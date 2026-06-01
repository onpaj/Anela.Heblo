using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;

public class CreateOrderShipmentHandler
    : IRequestHandler<CreateOrderShipmentRequest, CreateOrderShipmentResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackingOrderClient _orderClient;
    private readonly ShipmentLabelsSettings _settings;
    private readonly ILogger<CreateOrderShipmentHandler> _logger;

    public CreateOrderShipmentHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IOptions<ShipmentLabelsSettings> settings,
        ILogger<CreateOrderShipmentHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<CreateOrderShipmentResponse> Handle(
        CreateOrderShipmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Duplicate guard
            var existingLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(
                request.OrderCode, cancellationToken);

            if (existingLabels.Count > 0 && !request.ForceCreate)
            {
                return new CreateOrderShipmentResponse(
                    ErrorCodes.ShipmentAlreadyExists,
                    MapToDtos(existingLabels),
                    existingShipmentFound: true);
            }

            // 2. Load order and compute weight
            var order = await _orderClient.GetPackingOrderAsync(request.OrderCode, cancellationToken);
            if (order is null || order.Items.Count == 0)
            {
                return new CreateOrderShipmentResponse(ErrorCodes.ShipmentOrderWeightUnavailable);
            }

            var totalWeightGrams = order.Items.Sum(i => i.WeightGrams * i.Quantity);
            if (totalWeightGrams == 0)
            {
                return new CreateOrderShipmentResponse(ErrorCodes.ShipmentOrderWeightUnavailable);
            }

            var packageWeightGrams = Math.Max(totalWeightGrams, _settings.MinPackageWeightGrams);

            // 3. Resolve carrier
            var shippingOptions = await _shipmentClient.GetShippingOptionsAsync(
                request.OrderCode, cancellationToken);

            if (shippingOptions.Count == 0)
            {
                return new CreateOrderShipmentResponse(ErrorCodes.ShipmentCarrierNotResolved);
            }

            // 4. Create shipment
            var command = new CreateShipmentCommand
            {
                OrderCode = request.OrderCode,
                CarrierCode = shippingOptions[0].CarrierCode,
                Package = new ShipmentPackage
                {
                    WidthMm = _settings.DefaultPackageWidthMm,
                    HeightMm = _settings.DefaultPackageHeightMm,
                    DepthMm = _settings.DefaultPackageDepthMm,
                    WeightGrams = packageWeightGrams,
                },
            };

            CreatedShipment created;
            try
            {
                created = await _shipmentClient.CreateShipmentAsync(command, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Shoptet API failed to create shipment for order {OrderCode}", request.OrderCode);
                return new CreateOrderShipmentResponse(ErrorCodes.ShipmentCreationFailed);
            }

            // 5. Fetch label with one retry
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

            return new CreateOrderShipmentResponse(
                created.ShipmentGuid,
                created.Status,
                labelReady,
                MapToDtos(labels));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error creating shipment for order {OrderCode}", request.OrderCode);
            return new CreateOrderShipmentResponse(ErrorCodes.InternalServerError);
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
