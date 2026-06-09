namespace Anela.Heblo.Domain.Features.Logistics;

public interface IShippingMethodCatalog
{
    IReadOnlyList<(Carriers Carrier, DeliveryHandling Handling)> GetAvailableDeliveryOptions();

    IReadOnlyList<string> GetShippingCodesForCarrier(Carriers carrier);

    Carriers? ResolveCarrier(string shippingProviderCode);
}
