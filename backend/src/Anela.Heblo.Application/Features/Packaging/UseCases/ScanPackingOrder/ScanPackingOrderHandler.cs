using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;

public class ScanPackingOrderHandler : IRequestHandler<ScanPackingOrderRequest, ScanPackingOrderResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackingOrderClient _orderClient;
    private readonly IEshopOrderClient _eshopOrderClient;
    private readonly ShipmentLabelsSettings _shipmentSettings;
    private readonly ShoptetOrdersSettings _orderSettings;
    private readonly ILogger<ScanPackingOrderHandler> _logger;

    public ScanPackingOrderHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IEshopOrderClient eshopOrderClient,
        IOptions<ShipmentLabelsSettings> shipmentSettings,
        IOptions<ShoptetOrdersSettings> orderSettings,
        ILogger<ScanPackingOrderHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _eshopOrderClient = eshopOrderClient;
        _shipmentSettings = shipmentSettings.Value;
        _orderSettings = orderSettings.Value;
        _logger = logger;
    }

    public async Task<ScanPackingOrderResponse> Handle(ScanPackingOrderRequest request, CancellationToken ct)
    {
        var order = await _orderClient.GetPackingOrderAsync(request.OrderCode, ct);
        if (order is null)
            return new ScanPackingOrderResponse(ErrorCodes.ShoptetOrderNotFound);

        var isEligible = order.StatusId == _orderSettings.PackingStateId;
        var orderData = new ScanOrderData
        {
            Code = order.Code,
            CustomerName = order.CustomerName,
            ShippingMethodName = order.ShippingMethodName,
            Cooling = order.Cooling,
            IsCooled = order.IsCooled,
            CustomerNote = order.CustomerNote,
            EshopNote = order.EshopNote,
            Items = order.Items,
            Eligibility = new ScanOrderEligibility
            {
                IsEligible = isEligible,
                WarningTitle = isEligible ? null : "Objednávka není ve stavu „Balí se“",
                WarningBody = isEligible ? null : "Tuto objednávku nezpracovávejte, dokud nebude ve správném stavu.",
            },
        };

        if (!isEligible)
            return new ScanPackingOrderResponse(orderData);

        var existingLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        if (existingLabels.Count > 0)
        {
            var existingShipment = new ScanShipmentData
            {
                ShipmentGuid = existingLabels[0].ShipmentGuid,
                Packages = existingLabels
                    .Select(l => new ScanShipmentPackage
                    {
                        Name = l.PackageName,
                        TrackingNumber = l.TrackingNumber,
                        LabelUrl = l.LabelUrl,
                        LabelZpl = l.LabelZpl,
                    })
                    .ToList(),
                AlreadyExisted = true,
            };
            await TryMarkAsPackedAsync(request.OrderCode, ct);
            return new ScanPackingOrderResponse(orderData, existingShipment);
        }

        var totalWeightGrams = order.Items.Sum(i => i.WeightGrams * i.Quantity);
        if (totalWeightGrams == 0)
            return new ScanPackingOrderResponse(ErrorCodes.ShipmentOrderWeightUnavailable);

        var weightGrams = Math.Max(totalWeightGrams, _shipmentSettings.MinPackageWeightGrams);

        var options = await _shipmentClient.GetShippingOptionsAsync(request.OrderCode, ct);
        if (options.Count == 0)
            return new ScanPackingOrderResponse(ErrorCodes.ShipmentCarrierNotResolved);

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
            _logger.LogError(ex, "Failed to create shipment for order {OrderCode}", request.OrderCode);
            return new ScanPackingOrderResponse(ErrorCodes.ShipmentCreationFailed);
        }

        // Single fetch for package names + carrier label URLs (FE prints directly from the CDN).
        var newLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        var packages = newLabels.Count > 0
            ? newLabels.Select(l => new ScanShipmentPackage
            {
                Name = l.PackageName,
                TrackingNumber = l.TrackingNumber,
                LabelUrl = l.LabelUrl,
                LabelZpl = l.LabelZpl,
            }).ToList()
            : [new ScanShipmentPackage { Name = "PKG-1" }];

        await TryMarkAsPackedAsync(request.OrderCode, ct);
        return new ScanPackingOrderResponse(orderData, new ScanShipmentData
        {
            ShipmentGuid = createdShipment.ShipmentGuid,
            Packages = packages,
            AlreadyExisted = false,
        });
    }

    private async Task TryMarkAsPackedAsync(string orderCode, CancellationToken ct)
    {
        try
        {
            await _eshopOrderClient.UpdateStatusAsync(orderCode, _orderSettings.PackedStateId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update order {OrderCode} to packed status {StatusId}",
                orderCode, _orderSettings.PackedStateId);
        }
    }
}
