namespace Anela.Heblo.Application.Features.ShipmentLabels;

public interface IShipmentClient
{
    Task<IReadOnlyList<ShipmentLabel>> GetLabelsByOrderCodeAsync(string orderCode, CancellationToken ct = default);

    /// <summary>
    /// Returns the tracking number of the latest active (non-cancelled) shipment for the order,
    /// or null if no active shipment has a tracking number yet. Used to backfill tracking numbers
    /// that Shoptet had not generated at scan time — matching by shipment, not by package name
    /// (package names are non-unique within an order and change once the carrier label is generated).
    /// </summary>
    Task<string?> GetLatestActiveTrackingNumberAsync(string orderCode, CancellationToken ct = default);

    /// <summary>
    /// Returns true if the order has at least one shipment whose status is "delivered".
    /// Uses GET /api/shipments?orderCode={code}; status values follow the Shoptet lifecycle
    /// documented in docs/integrations/shoptet-api.md.
    /// </summary>
    Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default);

    Task<IReadOnlyList<ShippingOption>> GetShippingOptionsAsync(string orderCode, CancellationToken ct = default);

    Task<CreatedShipment> CreateShipmentAsync(CreateShipmentCommand command, CancellationToken ct = default);

    Task CancelShipmentAsync(Guid shipmentGuid, CancellationToken ct = default);
}
