using System.Globalization;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Packaging.Services;

public class ShipmentCreationService : IShipmentCreationService
{
    private const int MaxPackages = 10;

    private readonly IShipmentClient _shipmentClient;
    private readonly IPackageRepository _packageRepository;
    private readonly IAuthorizationRepository _authRepo;
    private readonly ICurrentUserService _currentUserService;
    private readonly ShipmentLabelsSettings _shipmentSettings;
    private readonly ILogger<ShipmentCreationService> _logger;

    public ShipmentCreationService(
        IShipmentClient shipmentClient,
        IPackageRepository packageRepository,
        IAuthorizationRepository authRepo,
        ICurrentUserService currentUserService,
        IOptions<ShipmentLabelsSettings> shipmentSettings,
        ILogger<ShipmentCreationService> logger)
    {
        _shipmentClient = shipmentClient;
        _packageRepository = packageRepository;
        _authRepo = authRepo;
        _currentUserService = currentUserService;
        _shipmentSettings = shipmentSettings.Value;
        _logger = logger;
    }

    public async Task<ShipmentCreationResult> CreateAndPersistAsync(
        PackingOrder order,
        int numberOfPackages,
        Guid? packingUserId,
        CancellationToken ct)
    {
        if (numberOfPackages < 1 || numberOfPackages > MaxPackages)
            return new ShipmentCreationResult { IsSuccess = false, ErrorCode = ErrorCodes.InvalidPackageCount };

        var totalWeightGrams = order.Items.Sum(i => i.WeightGrams * i.Quantity);
        if (totalWeightGrams == 0)
        {
            // Carriers reject a 0 kg package; fall back to a default package weight.
            _logger.LogWarning(
                "Order {OrderCode} has no known item weights; using fallback package weight {Fallback}g",
                order.Code, _shipmentSettings.FallbackPackageWeightGrams);
            totalWeightGrams = _shipmentSettings.FallbackPackageWeightGrams;
        }

        var n = numberOfPackages;
        var perPackageWeightGrams = Math.Max(totalWeightGrams / n, _shipmentSettings.MinPackageWeightGrams);

        var options = await _shipmentClient.GetShippingOptionsAsync(order.Code, ct);
        if (options.Count == 0)
            return new ShipmentCreationResult { IsSuccess = false, ErrorCode = ErrorCodes.ShipmentCarrierNotResolved };

        var command = new CreateShipmentCommand
        {
            OrderCode = order.Code,
            CarrierCode = options[0].CarrierCode,
            PackageCount = n,
            Package = new ShipmentPackage
            {
                WidthCm = _shipmentSettings.DefaultPackageWidthCm,
                HeightCm = _shipmentSettings.DefaultPackageHeightCm,
                DepthCm = _shipmentSettings.DefaultPackageDepthCm,
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
            _logger.LogError(ex, "Failed to create shipment for order {OrderCode}", order.Code);
            return new ShipmentCreationResult { IsSuccess = false, ErrorCode = ErrorCodes.ShipmentCreationFailed };
        }

        // Single fetch for carrier tracking numbers + label URLs (FE prints directly from the CDN).
        // Shoptet generates labels asynchronously, so the response may contain fewer labels than
        // the requested `n`, and — since a prior shipment for this order may have just been
        // cancelled (Reset) — the fetch can also still return stale labels for the cancelled
        // shipment(s). Filter to this shipment's GUID before padding to exactly `n` entries so
        // the FE shows the correct "X/N" counter without ever mixing in a cancelled shipment's
        // label; packages with no label yet get a null-fields entry (the FE's 404 retry path
        // handles the "carrier not ready" case).
        var fetchedLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(order.Code, ct);
        var matchingLabels = fetchedLabels
            .Where(l => l.ShipmentGuid == createdShipment.ShipmentGuid)
            .ToList();

        var paddedLabels = Enumerable.Range(1, n)
            .Select(i => i <= matchingLabels.Count
                ? matchingLabels[i - 1]
                : new ShipmentLabel
                {
                    ShipmentGuid = createdShipment.ShipmentGuid,
                    OrderCode = order.Code,
                    PackageName = string.Empty,
                })
            .ToList();

        Guid? packedByUserId;
        string? packedBy;
        if (packingUserId is { } requestedPackerId)
        {
            var packer = await _authRepo.GetUserByIdAsync(requestedPackerId, ct);
            if (packer is null || !packer.IsActive || !packer.CanPack)
                return new ShipmentCreationResult { IsSuccess = false, ErrorCode = ErrorCodes.PackingUserNotEligible };

            packedByUserId = packer.Id;
            packedBy = packer.DisplayName;
        }
        else
        {
            packedByUserId = null;
            packedBy = _currentUserService.GetCurrentUser().Email;
        }

        await PersistPackagesAsync(
            order.Code,
            order.CustomerName,
            command.CarrierCode,
            options[0].Name,
            createdShipment.ShipmentGuid,
            paddedLabels,
            packedByUserId,
            packedBy,
            ct);

        return new ShipmentCreationResult
        {
            IsSuccess = true,
            ShipmentGuid = createdShipment.ShipmentGuid,
            CarrierCode = command.CarrierCode,
            CarrierName = options[0].Name,
            Labels = paddedLabels,
        };
    }

    private async Task PersistPackagesAsync(
        string orderCode,
        string customerName,
        string carrierCode,
        string? carrierName,
        Guid shipmentGuid,
        IReadOnlyList<ShipmentLabel> paddedLabels,
        Guid? packedByUserId,
        string? packedBy,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Carrier package names are not unique per package (custom-packaging shipments
        // report the same "Vlastní balení" name for every package), so a 1-based index
        // within the order is used as the unique PackageNumber. The carrier's real
        // identifier is preserved in TrackingNumber. Rows are built from the padded
        // (n-length) list, not the raw fetched-label count, so a package whose label
        // Shoptet hasn't generated yet still gets a row (TrackingNumber = null) that
        // FillTrackingNumbersJob can later backfill.
        var packages = paddedLabels
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
            await _packageRepository.ReplacePackagesForOrderAsync(orderCode, packages, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist {PackageCount} Package row(s) for order {OrderCode} (shipment {ShipmentGuid})",
                packages.Count, orderCode, shipmentGuid);
        }
    }
}
