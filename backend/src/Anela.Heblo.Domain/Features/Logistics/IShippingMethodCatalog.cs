namespace Anela.Heblo.Domain.Features.Logistics;

public interface IShippingMethodCatalog
{
    IReadOnlyList<(Carriers Carrier, DeliveryHandling Handling)> GetAvailableDeliveryOptions();

    IReadOnlyList<string> GetShippingCodesForCarrier(Carriers carrier);

    Carriers? ResolveCarrier(string shippingProviderCode);

    /// <summary>Resolves the carrier of an e-shop shipping method GUID; null when the GUID is unknown.</summary>
    Carriers? ResolveCarrierByShippingGuid(string shippingGuid);
}
