namespace Anela.Heblo.Application.Features.ShipmentLabels;

public interface IShipmentClient
{
    Task<IReadOnlyList<ShipmentLabel>> GetLabelsByOrderCodeAsync(string orderCode, CancellationToken ct = default);

    Task<IReadOnlyList<ShippingOption>> GetShippingOptionsAsync(string orderCode, CancellationToken ct = default);

    Task<CreatedShipment> CreateShipmentAsync(CreateShipmentCommand command, CancellationToken ct = default);

    Task CancelShipmentAsync(Guid shipmentGuid, CancellationToken ct = default);
}
