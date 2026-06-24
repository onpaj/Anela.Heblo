using System.Globalization;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
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
    private readonly IAuthorizationRepository _authRepo;

    public ScanPackingOrderHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IEshopOrderClient eshopOrderClient,
        IOptions<ShipmentLabelsSettings> shipmentSettings,
        ILogger<ScanPackingOrderHandler> logger,
        IPackageRepository packageRepository,
        ICurrentUserService currentUserService,
        IAuthorizationRepository authRepo)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _eshopOrderClient = eshopOrderClient;
        _shipmentSettings = shipmentSettings.Value;
        _logger = logger;
        _packageRepository = packageRepository;
        _currentUserService = currentUserService;
        _authRepo = authRepo;
    }

    public async Task<ScanPackingOrderResponse> Handle(ScanPackingOrderRequest request, CancellationToken ct)
    {
        const int maxPackages = 10;
        if (request.NumberOfPackages < 1 || request.NumberOfPackages > maxPackages)
            return new ScanPackingOrderResponse(ErrorCodes.InvalidPackageCount);

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
            await BackfillExistingShipmentPackagesAsync(
                request.OrderCode, orderData.CustomerName, existingLabels, request.PackingUserId, ct);
            await TryMarkAsPackedAsync(request.OrderCode, ct);
            return new ScanPackingOrderResponse(orderData, existingShipment);
        }

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
            return new ScanPackingOrderResponse(ErrorCodes.ShipmentCarrierNotResolved);

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
            _logger.LogError(ex, "Failed to create shipment for order {OrderCode}", request.OrderCode);
            return new ScanPackingOrderResponse(ErrorCodes.ShipmentCreationFailed);
        }

        // Single fetch for carrier tracking numbers + label URLs (FE prints directly from the CDN).
        // Shoptet generates labels asynchronously, so the response may contain fewer labels than
        // the requested `n`. Always produce exactly `n` entries so the FE shows the correct
        // "X/N" counter; packages with no label yet get null tracking + URLs
        // (the FE's 404 retry path handles the "carrier not ready" case).
        var newLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        var packages = Enumerable.Range(1, n)
            .Select(i =>
            {
                var label = i <= newLabels.Count ? newLabels[i - 1] : null;
                return new ScanShipmentPackage
                {
                    TrackingNumber = label?.TrackingNumber,
                    LabelUrl = label?.LabelUrl,
                    LabelZpl = label?.LabelZpl,
                };
            })
            .ToList();

        if (request.PackingUserId is { } requestedPackerId)
        {
            var packer = await _authRepo.GetUserByIdAsync(requestedPackerId, ct);
            if (packer is null || !packer.IsActive || !packer.CanPack)
                return new ScanPackingOrderResponse(ErrorCodes.PackingUserNotEligible);
        }

        await PersistPackagesAsync(
            request.OrderCode,
            orderData.CustomerName,
            command.CarrierCode,
            options[0].Name,
            createdShipment.ShipmentGuid,
            newLabels,
            request.PackingUserId,
            ct);

        var pendingCompletion = n >= 2;
        if (!pendingCompletion)
        {
            await TryMarkAsPackedAsync(request.OrderCode, ct);
        }

        return new ScanPackingOrderResponse(orderData, new ScanShipmentData
        {
            ShipmentGuid = createdShipment.ShipmentGuid,
            Packages = packages,
            AlreadyExisted = false,
            PendingCompletion = pendingCompletion,
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

    private async Task<(Guid? userId, string? name)> ResolvePackerAsync(Guid? packingUserId, CancellationToken ct)
    {
        if (packingUserId is { } id)
        {
            var user = await _authRepo.GetUserByIdAsync(id, ct);
            if (user is not null)
                return (user.Id, user.DisplayName);
        }
        return (null, _currentUserService.GetCurrentUser().Email);
    }

    /// <summary>
    /// Backfills Package rows for an order whose Shoptet shipment already exists (reprint path).
    /// Idempotent and best-effort: never throws, so a reprint always returns the existing shipment.
    /// </summary>
    private async Task BackfillExistingShipmentPackagesAsync(
        string orderCode,
        string customerName,
        IReadOnlyList<ShipmentLabel> existingLabels,
        Guid? packingUserId,
        CancellationToken cancellationToken)
    {
        if (existingLabels.Count == 0)
            return;

        try
        {
            var options = await _shipmentClient.GetShippingOptionsAsync(orderCode, cancellationToken);
            var carrierCode = options.Count > 0 ? options[0].CarrierCode : string.Empty;
            var carrierName = options.Count > 0 ? options[0].Name : null;

            var now = DateTimeOffset.UtcNow;
            var (packedByUserId, packedBy) = await ResolvePackerAsync(packingUserId, cancellationToken);

            var packages = existingLabels
                .Select(label => new Package
                {
                    OrderCode = orderCode,
                    CustomerName = customerName,
                    PackageNumber = label.PackageName,
                    TrackingNumber = label.TrackingNumber,
                    ShippingProviderCode = carrierCode,
                    ShippingProviderName = carrierName,
                    ShipmentGuid = label.ShipmentGuid,
                    PackedAt = now,
                    PackedBy = packedBy,
                    PackedByUserId = packedByUserId,
                    CreatedAt = now,
                })
                .ToList();

            await _packageRepository.AddMissingAsync(packages, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to backfill Package rows for existing shipment of order {OrderCode}", orderCode);
        }
    }

    private async Task PersistPackagesAsync(
        string orderCode,
        string customerName,
        string carrierCode,
        string carrierName,
        Guid shipmentGuid,
        IReadOnlyList<ShipmentLabel> labels,
        Guid? packingUserId,
        CancellationToken cancellationToken)
    {
        if (labels.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var (packedByUserId, packedBy) = await ResolvePackerAsync(packingUserId, cancellationToken);

        // Carrier package names are not unique per package (custom-packaging shipments
        // report the same "Vlastní balení" name for every package), so a 1-based index
        // within the order is used as the unique PackageNumber. The carrier's real
        // identifier is preserved in TrackingNumber.
        var packages = labels
            .Select((label, index) => new Package
            {
                OrderCode = orderCode,
                CustomerName = customerName,
                PackageNumber = (index + 1).ToString(CultureInfo.InvariantCulture),
                TrackingNumber = label.TrackingNumber,
                ShippingProviderCode = carrierCode,
                ShippingProviderName = carrierName,
                ShipmentGuid = shipmentGuid,
                PackedAt = now,
                PackedBy = packedBy,
                PackedByUserId = packedByUserId,
                CreatedAt = now,
            })
            .ToList();

        try
        {
            await _packageRepository.ReplacePackagesForOrderAsync(orderCode, packages, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist {PackageCount} Package row(s) for order {OrderCode}",
                packages.Count, orderCode);
        }
    }
}
