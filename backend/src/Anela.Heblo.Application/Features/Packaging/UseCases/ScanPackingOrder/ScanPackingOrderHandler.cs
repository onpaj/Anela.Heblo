using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
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
    private readonly ILogger<ScanPackingOrderHandler> _logger;
    private readonly IPackageRepository _packageRepository;
    private readonly ICurrentUserService _currentUserService;

    public ScanPackingOrderHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IEshopOrderClient eshopOrderClient,
        IOptions<ShipmentLabelsSettings> shipmentSettings,
        ILogger<ScanPackingOrderHandler> logger,
        IPackageRepository packageRepository,
        ICurrentUserService currentUserService)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _eshopOrderClient = eshopOrderClient;
        _shipmentSettings = shipmentSettings.Value;
        _logger = logger;
        _packageRepository = packageRepository;
        _currentUserService = currentUserService;
    }

    public async Task<ScanPackingOrderResponse> Handle(ScanPackingOrderRequest request, CancellationToken ct)
    {
        var order = await _orderClient.GetPackingOrderAsync(request.OrderCode, ct);
        if (order is null)
            return new ScanPackingOrderResponse(ErrorCodes.ShoptetOrderNotFound);

        var isEligible = order.IsEligibleForPacking;
        var orderData = new ScanOrderData
        {
            Code = order.Code,
            CustomerName = order.CustomerName,
            ShippingMethodName = order.ShippingMethodName,
            Cooling = order.Cooling,
            IsCooled = order.IsCooled,
            CustomerNote = order.CustomerNote,
            EshopNote = order.EshopNote,
            ShippingAddress = BuildShippingAddress(order),
            Items = order.Items
                .Select(i => new ScanPackingOrderItemDto
                {
                    Name = i.Name,
                    Quantity = i.Quantity,
                    ImageUrl = i.ImageUrl,
                    SetName = i.SetName,
                })
                .ToList(),
            Eligibility = new ScanOrderEligibility
            {
                IsEligible = isEligible,
                WarningTitle = isEligible ? null : "Objednávka není ve stavu „Balí se“",
                WarningBody = isEligible ? null : "Tuto objednávku nezpracovávejte, dokud nebude ve správném stavu.",
            },
        };

        var existingLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        ScanShipmentData? existingShipment = existingLabels.Count > 0
            ? new ScanShipmentData
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
            }
            : null;

        if (!isEligible)
        {
            // Already-packed order rescanned for review: include shipment if it exists.
            // Don't mark-as-packed; the order has already moved past the packing state.
            return existingShipment is null
                ? new ScanPackingOrderResponse(orderData)
                : new ScanPackingOrderResponse(orderData, existingShipment);
        }

        if (existingShipment is not null)
        {
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

        await PersistPackagesAsync(
            request.OrderCode,
            orderData.CustomerName,
            command.CarrierCode,
            createdShipment.ShipmentGuid,
            newLabels,
            ct);

        await TryMarkAsPackedAsync(request.OrderCode, ct);
        return new ScanPackingOrderResponse(orderData, new ScanShipmentData
        {
            ShipmentGuid = createdShipment.ShipmentGuid,
            Packages = packages,
            AlreadyExisted = false,
        });
    }

    private static ShippingAddress? BuildShippingAddress(PackingOrder order)
    {
        var street = string.IsNullOrEmpty(order.ShippingStreet) ? null : order.ShippingStreet;
        var city = string.IsNullOrEmpty(order.ShippingCity) ? null : order.ShippingCity;
        var zip = string.IsNullOrEmpty(order.ShippingZip) ? null : order.ShippingZip;

        if (street is null && city is null && zip is null)
            return null;

        return new ShippingAddress
        {
            Street = street,
            City = city,
            Zip = zip,
        };
    }

    private async Task TryMarkAsPackedAsync(string orderCode, CancellationToken ct)
    {
        try
        {
            await _eshopOrderClient.MarkAsPackedAsync(orderCode, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark order {OrderCode} as packed", orderCode);
        }
    }

    private async Task PersistPackagesAsync(
        string orderCode,
        string customerName,
        string carrierCode,
        Guid shipmentGuid,
        IReadOnlyList<ShipmentLabel> labels,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var packedBy = _currentUserService.GetCurrentUser().Email;

        foreach (var label in labels)
        {
            try
            {
                await _packageRepository.AddAsync(new Package
                {
                    OrderCode = orderCode,
                    CustomerName = customerName,
                    PackageNumber = label.PackageName,
                    TrackingNumber = label.TrackingNumber,
                    ShippingProviderCode = carrierCode,
                    ShippingProviderName = null,
                    ShipmentGuid = shipmentGuid,
                    PackedAt = now,
                    PackedBy = packedBy,
                    CreatedAt = now,
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to persist Package row for order {OrderCode} package {PackageName}",
                    orderCode, label.PackageName);
            }
        }
    }
}
