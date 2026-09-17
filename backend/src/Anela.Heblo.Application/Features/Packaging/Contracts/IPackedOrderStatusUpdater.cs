namespace Anela.Heblo.Application.Features.Packaging.Contracts;

public interface IPackedOrderStatusUpdater
{
    /// <summary>
    /// Transitions the order to the configured "packed" state (Shoptet "Zabaleno", id 52 by default).
    /// Mirrors IEshopOrderClient.MarkAsPackedAsync; Packaging depends only on this narrower surface
    /// via the consumer-owns-contract pattern (see IShipmentDeliveryChecker / ILeafletKnowledgeSource).
    /// </summary>
    Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);
}
