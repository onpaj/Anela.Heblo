namespace Anela.Heblo.Domain.Features.Logistics;

public interface IShippingMethodCatalog
{
    IReadOnlyList<(Carriers Carrier, DeliveryHandling Handling)> GetAvailableDeliveryOptions();
}
