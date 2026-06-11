using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ShippingMethodCatalog : IShippingMethodCatalog
{
    public IReadOnlyList<(Carriers Carrier, DeliveryHandling Handling)> GetAvailableDeliveryOptions()
    {
        return ShippingMethodRegistry.ShippingList
            .Where(m => m.Carrier != Carriers.Osobak && !m.Name.Contains("_EXPORT"))
            .Select(m => (m.Carrier, Handling: ShippingMethodRegistry.ResolveDeliveryHandling(m)))
            .Where(x => x.Handling.HasValue)
            .Select(x => (x.Carrier, x.Handling!.Value))
            .Distinct()
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<string> GetShippingCodesForCarrier(Carriers carrier)
    {
        return ShippingMethodRegistry.ShippingList
            .Where(m => m.Carrier == carrier)
            .Select(m => m.Id.ToString())
            .ToList()
            .AsReadOnly();
    }

    public Carriers? ResolveCarrier(string shippingProviderCode)
    {
        if (!int.TryParse(shippingProviderCode, out var id))
            return null;

        return ShippingMethodRegistry.ShippingList
            .FirstOrDefault(m => m.Id == id)?.Carrier;
    }
}
