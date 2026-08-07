using Anela.Heblo.Application.Features.ShoptetOrders;

namespace Anela.Heblo.Application.Features.Packaging.Services;

/// <summary>
/// Owns the shared "resolve weight → resolve carrier → create shipment → fetch/filter/pad
/// labels → resolve packer → persist Package rows" sequence used by both
/// ScanPackingOrderHandler (create path) and ResetOrderShipmentHandler.
/// </summary>
public interface IShipmentCreationService
{
    /// <summary>
    /// Creates a carrier shipment for <paramref name="order"/> and persists the resulting
    /// Package rows. The caller must have already fetched <paramref name="order"/> — this
    /// method never calls IPackingOrderClient itself. <paramref name="packingUserId"/> is
    /// null when no specific packer is being attributed (e.g. always for Reset today).
    /// </summary>
    Task<ShipmentCreationResult> CreateAndPersistAsync(
        PackingOrder order,
        int numberOfPackages,
        Guid? packingUserId,
        CancellationToken ct);
}
