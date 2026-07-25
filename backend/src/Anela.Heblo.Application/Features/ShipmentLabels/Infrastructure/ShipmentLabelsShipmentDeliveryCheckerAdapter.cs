using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

namespace Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure;

internal sealed class ShipmentLabelsShipmentDeliveryCheckerAdapter : IShipmentDeliveryChecker
{
    private readonly IShipmentClient _shipmentClient;

    public ShipmentLabelsShipmentDeliveryCheckerAdapter(IShipmentClient shipmentClient)
    {
        _shipmentClient = shipmentClient;
    }

    public Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default)
        => _shipmentClient.HasDeliveredShipmentAsync(orderCode, ct);
}
