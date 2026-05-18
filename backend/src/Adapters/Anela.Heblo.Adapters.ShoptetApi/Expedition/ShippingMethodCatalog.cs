using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ShippingMethodCatalog : IShippingMethodCatalog
{
    public IReadOnlyList<(Carriers Carrier, DeliveryHandling Handling)> GetAvailableDeliveryOptions()
    {
        return ShippingMethodRegistry.ShippingList
            .Where(m => m.Carrier != Carriers.Osobak && !m.Name.Contains("_EXPORT"))
            .Select(m =>
            {
                DeliveryHandling? handling =
                    m.Name.Contains("DO_RUKY") ? DeliveryHandling.NaRuky :
                    m.Name.Contains("PARCELSHOP") || m.Name.Contains("ZPOINT") ? DeliveryHandling.Box :
                    (DeliveryHandling?)null;

                return (m.Carrier, Handling: handling);
            })
            .Where(x => x.Handling.HasValue)
            .Select(x => (x.Carrier, x.Handling!.Value))
            .Distinct()
            .ToList()
            .AsReadOnly();
    }
}
