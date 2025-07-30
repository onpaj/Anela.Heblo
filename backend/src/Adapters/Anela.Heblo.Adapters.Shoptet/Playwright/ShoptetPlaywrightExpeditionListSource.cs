using Anela.Heblo.Adapters.Shoptet.Playwright.Model;
using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Anela.Heblo.Application.Domain.Logistics;
using Anela.Heblo.Application.Domain.Logistics.Picking;

namespace Anela.Heblo.Adapters.Shoptet.Playwright;

public class ShoptetPlaywrightExpeditionListSource : IPickingListSource
{
    private const string SourceStateId = "-2"; // Vyrizuje se
    //private const string SourceStateId = "55"; // K Expedici
    //private const string SourceStateId = "26"; // Bali se
    //private const string DesiredStateId = "26"; // Bali se
    private const string DesiredStateId = "55"; // K Expedici
    
    
    private readonly PrintPickingListScenario _printScenario;

    private const int ZASILKOVNA_DO_RUKY = 21;
    private const int ZASILKOVNA_ZPOINT = 15;
    private const int ZASILKOVNA_DO_RUKY_SK = 385;
    private const int ZASILKOVNA_DO_RUKY_CHLAZENY = 370;
    private const int ZASILKOVNA_ZPOINT_CHLAZENY = 373;
    private const int ZASILKOVNA_DO_RUKY_SK_CHLAZENY = 388;

    private const int ZASILKOVNA_ZPOINT_CHLAZENY_ZDARMA = 481;
    private const int ZASILKOVNA_ZPOINT_ZDARMA = 487;

    
    private const int PPL_DO_RUKY = 6;
    private const int PPL_PARCELSHOP = 80;
    private const int PPL_EXPORT = 86;
    private const int PPL_DO_RUKY_CHLAZENY = 358;
    private const int PPL_PARCELSHOP_CHLAZENY = 361;
    private const int PPL_EXPORT_CHLAZENY = 379;

    private const int GLS_DO_RUKY = 97;
    private const int GLS_EXPORT = 109;

    private const int OSOBAK = 4;


    private readonly List<Shipping> ShippingList = new()
    {
        new Shipping { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY", Id = ZASILKOVNA_DO_RUKY },
        new Shipping { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT", Id = ZASILKOVNA_ZPOINT}, 
        new Shipping { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_SK", Id = ZASILKOVNA_DO_RUKY_SK },
        new Shipping { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_CHLAZENY", Id = ZASILKOVNA_DO_RUKY_CHLAZENY },
        new Shipping { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_CHLAZENY", Id = ZASILKOVNA_ZPOINT_CHLAZENY },
        new Shipping { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_SK_CHLAZENY", Id = ZASILKOVNA_DO_RUKY_SK_CHLAZENY },
        new Shipping { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_ZDARMA", Id = ZASILKOVNA_ZPOINT_ZDARMA },
        new Shipping { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_CHLAZENY_ZDARMA", Id = ZASILKOVNA_ZPOINT_CHLAZENY_ZDARMA },
        
        new Shipping { Carrier = Carriers.PPL, Name = "PPL_DO_RUKY", Id = PPL_DO_RUKY},
        new Shipping { Carrier = Carriers.PPL, Name = "PPL_PARCELSHOP", Id = PPL_PARCELSHOP},
        new Shipping { Carrier = Carriers.PPL, Name = "PPL_EXPORT", Id = PPL_EXPORT },
        new Shipping { Carrier = Carriers.PPL, Name = "PPL_DO_RUKY_CHLAZENY", Id = PPL_DO_RUKY_CHLAZENY },
        new Shipping { Carrier = Carriers.PPL, Name = "PPL_PARCELSHOP_CHLAZENY", Id = PPL_PARCELSHOP_CHLAZENY },
        new Shipping { Carrier = Carriers.PPL, Name = "PPL_EXPORT_CHLAZENY", Id = PPL_EXPORT_CHLAZENY },
        
        new Shipping { Carrier = Carriers.GLS, Name = "GLS_DO_RUKY", Id = GLS_DO_RUKY},
        new Shipping { Carrier = Carriers.GLS, Name = "GLS_EXPORT", Id = GLS_EXPORT },
        
        new Shipping { Carrier = Carriers.Osobak, Name = "OSOBAK", Id = OSOBAK, PageSize = 1}
    };
    
    
    public ShoptetPlaywrightExpeditionListSource(PrintPickingListScenario printScenario)
    {
        _printScenario = printScenario;
    }
    
    public async Task<PrintPickingListResult> CreatePickingList(PrintPickingListRequest request, CancellationToken cancellationToken = default)
    {
        var shippings = ShippingList;

        if (request.Carriers.Any())
            shippings = shippings.Where(w => request.Carriers.Contains(w.Carrier)).ToList();

        var result = await _printScenario.RunAsync(shippings, sourceStateId: request.SourceStateId, desiredStateId: request.ChangeOrderState ? request.DesiredStateId : null, maxPageSize: shippings.Max(m => m.PageSize));

        return result;
    }
}