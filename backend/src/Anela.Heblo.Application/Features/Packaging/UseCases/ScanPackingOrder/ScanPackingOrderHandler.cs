using Anela.Heblo.Application.Features.Packaging.Services;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;

public class ScanPackingOrderHandler : IRequestHandler<ScanPackingOrderRequest, ScanPackingOrderResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackingOrderClient _orderClient;
    private readonly IEshopOrderClient _eshopOrderClient;
    private readonly ILogger<ScanPackingOrderHandler> _logger;
    private readonly IPackageRepository _packageRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuthorizationRepository _authRepo;
    private readonly IShipmentCreationService _shipmentCreationService;

    public ScanPackingOrderHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IEshopOrderClient eshopOrderClient,
        ILogger<ScanPackingOrderHandler> logger,
        IPackageRepository packageRepository,
        ICurrentUserService currentUserService,
        IAuthorizationRepository authRepo,
        IShipmentCreationService shipmentCreationService)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _eshopOrderClient = eshopOrderClient;
        _logger = logger;
        _packageRepository = packageRepository;
        _currentUserService = currentUserService;
        _authRepo = authRepo;
        _shipmentCreationService = shipmentCreationService;
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

        var result = await _shipmentCreationService.CreateAndPersistAsync(
            order, request.NumberOfPackages, request.PackingUserId, ct);
        if (!result.IsSuccess)
            return new ScanPackingOrderResponse(result.ErrorCode!.Value);

        var packages = result.Labels
            .Select(label => new ScanShipmentPackage
            {
                TrackingNumber = label.TrackingNumber,
                LabelUrl = label.LabelUrl,
                LabelZpl = label.LabelZpl,
            })
            .ToList();

        // The Shoptet "Zabaleno" (52) transition is deferred to the FE, which calls
        // .../packing/complete only after every carrier label is confirmed fetched & printed.
        // A successful CreateAndPersistAsync means Shoptet accepted the request, NOT that a
        // usable label was produced (labels generate asynchronously and can fail). Marking
        // here would move the order to "Zabaleno" even when no label exists. Single- and
        // multi-package orders share this deferred path.
        return new ScanPackingOrderResponse(orderData, new ScanShipmentData
        {
            ShipmentGuid = result.ShipmentGuid,
            Packages = packages,
            AlreadyExisted = false,
            PendingCompletion = true,
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
}
