using Anela.Heblo.Catalog.Sales;
using Anela.Heblo.Data;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Client.Clients.UserQueries;
using Rem.FlexiBeeSDK.Client.ResultFilters;
using Volo.Abp.ObjectMapping;

namespace Anela.Heblo.Adapters.Flexi.Sales;

public class FlexiCatalogSalesClient : UserQueryClient<CatalogSalesFlexiDto>, ICatalogSalesClient
{
    private readonly IObjectMapper _mapper;
    private readonly ISynchronizationContext _synchronizationContext;
    private const string DateFromParamName = "DATUM_OD";
    private const string DateToParamName = "DATUM_DO";
    
    public FlexiCatalogSalesClient(
        FlexiBeeSettings connection, 
        IHttpClientFactory httpClientFactory, 
        IResultHandler resultHandler, 
        IObjectMapper mapper,
        ISynchronizationContext synchronizationContext,
        ILogger<ReceivedInvoiceClient> logger
    ) 
        : base(connection, httpClientFactory, resultHandler, logger)
    {
        _mapper = mapper;
        _synchronizationContext = synchronizationContext;
    }

    protected override int QueryId => 37;
    
    public async Task<IList<CatalogSales>> GetAsync(DateTime dateFrom, DateTime dateTo, int limit = 0, CancellationToken cancellationToken = default)
    {
        var pars = new Dictionary<string, string>();
        pars.Add(DateFromParamName, dateFrom.ToString("yyyy-MM-dd"));
        pars.Add(DateToParamName, dateTo.ToString("yyyy-MM-dd"));
        pars.Add(LimitParamName, limit.ToString());
        
        var flexiSales = await GetAsync(pars, cancellationToken);
        _synchronizationContext.Submit(new CatalogSalesSyncData(flexiSales));

        return _mapper.Map<CatalogSalesFlexiDto, CatalogSales>(flexiSales);
    }
}