using Anela.Heblo.Application.Domain.Catalog.Sales;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Client.Clients.UserQueries;
using Rem.FlexiBeeSDK.Client.ResultFilters;

namespace Anela.Heblo.Adapters.Flexi.Sales;

public class FlexiCatalogSalesClient : UserQueryClient<CatalogSalesFlexiDto>, ICatalogSalesClient
{
    private readonly IMapper _mapper;
    private const string DateFromParamName = "DATUM_OD";
    private const string DateToParamName = "DATUM_DO";
    
    public FlexiCatalogSalesClient(
        FlexiBeeSettings connection, 
        IHttpClientFactory httpClientFactory, 
        IResultHandler resultHandler, 
        IMapper mapper,
        ILogger<ReceivedInvoiceClient> logger
    ) 
        : base(connection, httpClientFactory, resultHandler, logger)
    {
        _mapper = mapper;
    }

    protected override int QueryId => 37;
    
    public async Task<IList<CatalogSaleRecord>> GetAsync(DateTime dateFrom, DateTime dateTo, int limit = 0, CancellationToken cancellationToken = default)
    {
        var pars = new Dictionary<string, string>();
        pars.Add(DateFromParamName, dateFrom.ToString("yyyy-MM-dd"));
        pars.Add(DateToParamName, dateTo.ToString("yyyy-MM-dd"));
        pars.Add(LimitParamName, limit.ToString());
        
        var flexiSales = await GetAsync(pars, cancellationToken);
        
        // TODO Add Audit trace to log successful load

        return _mapper.Map<IList<CatalogSalesFlexiDto>, IList<CatalogSaleRecord>>(flexiSales);
    }
}