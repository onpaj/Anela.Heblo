using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

internal static class ExpeditionAddressValidator
{
    public static IReadOnlyList<string> GetMissingFields(ExpeditionAddress? address)
    {
        var missing = new List<string>();

        var hasName = !string.IsNullOrWhiteSpace(address?.FullName)
                      || !string.IsNullOrWhiteSpace(address?.Company);
        if (!hasName)
            missing.Add("jméno příjemce");
        if (string.IsNullOrWhiteSpace(address?.Street))
            missing.Add("ulice");
        if (string.IsNullOrWhiteSpace(address?.HouseNumber))
            missing.Add("číslo popisné");
        if (string.IsNullOrWhiteSpace(address?.City))
            missing.Add("město");
        if (string.IsNullOrWhiteSpace(address?.Zip))
            missing.Add("PSČ");

        return missing;
    }
}
