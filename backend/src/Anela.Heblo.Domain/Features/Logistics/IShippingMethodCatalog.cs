namespace Anela.Heblo.Domain.Features.Logistics;

public interface IShippingMethodCatalog
{
    IReadOnlyList<(Carriers Carrier, DeliveryHandling Handling)> GetAvailableDeliveryOptions();

    IReadOnlyList<string> GetShippingCodesForCarrier(Carriers carrier);

    /// <summary>
    /// Display names of the carrier's shipping methods. Packages created from the Shoptet delivery API
    /// store a per-order shipping id instead of the registry id, so the name is the reliable match.
    /// </summary>
    IReadOnlyList<string> GetShippingNamesForCarrier(Carriers carrier);

    Carriers? ResolveCarrier(string shippingProviderCode);

    /// <summary>Resolves the carrier of an e-shop shipping method GUID; null when the GUID is unknown.</summary>
    Carriers? ResolveCarrierByShippingGuid(string shippingGuid);
}
