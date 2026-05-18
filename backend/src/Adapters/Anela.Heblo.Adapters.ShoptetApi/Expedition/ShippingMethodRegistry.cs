using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

internal static class ShippingMethodRegistry
{
    // GUIDs discovered via: GET /api/eshop?include=shippingMethods (production store 269953/anela.cz)
    internal static readonly IReadOnlyList<ShippingMethod> ShippingList = new List<ShippingMethod>
    {
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY",              Id = 21,  Guids = ["f6610d4d-578d-11e9-beb1-002590dad85e"] }, // Zásilkovna (do ruky)
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT",               Id = 15,  Guids = ["7878c138-578d-11e9-beb1-002590dad85e", "389cea0b-40f1-11ea-beb1-002590dad85e"] }, // Zásilkovna Z-Point (retail + VO)
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_SK",           Id = 385, Guids = ["a6d9a6ce-0ede-11ee-b534-2a01067a25a9"] }, // Zásilkovna (do ruky) SK
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_CHLAZENY",     Id = 370, Guids = ["34d3f7d4-166f-11ee-b534-2a01067a25a9"] }, // Zásilkovna chlazený balík (do ruky)
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_CHLAZENY",      Id = 373, Guids = ["bac58d34-166f-11ee-b534-2a01067a25a9"] }, // Zásilkovna Z-Point chlazený balík - ZDARMA od 1500,-
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_SK_CHLAZENY",  Id = 388, Guids = ["75123baa-1671-11ee-b534-2a01067a25a9"] }, // Zásilkovna SK chlazený balík (do ruky)
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_ZDARMA",        Id = 487, Guids = ["79b9ef95-5e46-11f0-ae6d-9237d29d7242"] }, // Zásilkovna Z-Point - DOPRAVA ZDARMA
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_CHLAZENY_ZDARMA", Id = 481, Guids = ["db9bf927-5e44-11f0-ae6d-9237d29d7242"] }, // Zásilkovna Z-Point - PLATÍTE POUZE CHLADÍTKO
        new() { Carrier = Carriers.PPL,        Name = "PPL_DO_RUKY",                     Id = 6,   Guids = ["2ec88ea7-3fb0-11e2-a723-705ab6a2ba75", "389ce5b4-40f1-11ea-beb1-002590dad85e"] }, // PPL (do ruky) (retail + VO)
        new() { Carrier = Carriers.PPL,        Name = "PPL_PARCELSHOP",                  Id = 80,  Guids = ["c4e6c287-9a85-11ea-beb1-002590dad85e", "83372e07-9a86-11ea-beb1-002590dad85e"] }, // PPL ParcelShop (retail + VO)
        new() { Carrier = Carriers.PPL,        Name = "PPL_EXPORT",                      Id = 86,  Guids = ["f17a0a12-0ebe-11eb-933a-002590dad85e", "2fd96b91-1508-11eb-933a-002590dad85e"] }, // PPL Export (retail + VO)
        new() { Carrier = Carriers.PPL,        Name = "PPL_DO_RUKY_CHLAZENY",            Id = 358, Guids = ["05ea842d-166a-11ee-b534-2a01067a25a9"] }, // PPL chlazený balík (do ruky) - ZDARMA od 3000,-
        new() { Carrier = Carriers.PPL,        Name = "PPL_PARCELSHOP_CHLAZENY",         Id = 361, Guids = ["0d10802f-166c-11ee-b534-2a01067a25a9"] }, // PPL ParcelShop chlazený balík - ZDARMA od 1500,-
        new() { Carrier = Carriers.PPL,        Name = "PPL_EXPORT_CHLAZENY",             Id = 379, Guids = ["de70f0e4-1670-11ee-b534-2a01067a25a9"] }, // PPL Export chlazený balík (zahraničí)
        new() { Carrier = Carriers.GLS,        Name = "GLS_DO_RUKY",                     Id = 97,  Guids = ["138ec07f-0119-11ec-a39f-002590dc5efc", "b7e787c5-011d-11ec-a39f-002590dc5efc"] }, // GLS (do ruky) (retail + VO)
        new() { Carrier = Carriers.GLS,        Name = "GLS_EXPORT",                      Id = 109, Guids = ["c06835e6-165e-11ec-a39f-002590dc5efc", "bbbe7223-4ea8-11ec-a39f-002590dc5efc"] }, // GLS Export (retail + VO)
        new() { Carrier = Carriers.GLS,        Name = "GLS_PARCELSHOP",                  Id = 489, Guids = ["49b79aec-0118-11ec-a39f-002590dc5efc"] }, // GLS ParcelShop
        new() { Carrier = Carriers.Osobak,     Name = "OSOBAK",                          Id = 4,   Guids = ["8fdb2c89-3fae-11e2-a723-705ab6a2ba75", "389ce19e-40f1-11ea-beb1-002590dad85e"], MaxOrders = 1, MaxItems = int.MaxValue }, // Osobní odběr (retail + VO)
    };

    internal static readonly IReadOnlyDictionary<string, ShippingMethod> ByGuid =
        ShippingList
            .SelectMany(s => s.Guids.Select(g => (Guid: g, Method: s)))
            .ToDictionary(x => x.Guid, x => x.Method);

    internal static DeliveryHandling? ResolveDeliveryHandling(ShippingMethod method) =>
        method.Name.Contains("DO_RUKY") ? DeliveryHandling.NaRuky :
        method.Name.Contains("PARCELSHOP") || method.Name.Contains("ZPOINT") ? DeliveryHandling.Box :
        (DeliveryHandling?)null;
}
