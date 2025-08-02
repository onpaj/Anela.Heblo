using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Xcc.Audit;
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
    private readonly IDataLoadAuditService _auditService;
    private const string DateFromParamName = "DATUM_OD";
    private const string DateToParamName = "DATUM_DO";

    public FlexiCatalogSalesClient(
        FlexiBeeSettings connection,
        IHttpClientFactory httpClientFactory,
        IResultHandler resultHandler,
        IMapper mapper,
        ILogger<ReceivedInvoiceClient> logger,
        IDataLoadAuditService auditService
    )
        : base(connection, httpClientFactory, resultHandler, logger)
    {
        _mapper = mapper;
        _auditService = auditService;
    }

    protected override int QueryId => 37;

    public async Task<IList<CatalogSaleRecord>> GetAsync(DateTime dateFrom, DateTime dateTo, int limit = 0, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var parameters = new Dictionary<string, object>
        {
            ["dateFrom"] = dateFrom.ToString("yyyy-MM-dd"),
            ["dateTo"] = dateTo.ToString("yyyy-MM-dd"),
            ["limit"] = limit
        };

        try
        {
            var pars = new Dictionary<string, string>();
            pars.Add(DateFromParamName, dateFrom.ToString("yyyy-MM-dd"));
            pars.Add(DateToParamName, dateTo.ToString("yyyy-MM-dd"));
            pars.Add(LimitParamName, limit.ToString());

            var flexiSales = await GetAsync(pars, cancellationToken);
            var salesRecords = _mapper.Map<IList<CatalogSalesFlexiDto>, IList<CatalogSaleRecord>>(flexiSales);

            var duration = DateTime.UtcNow - startTime;
            await _auditService.LogDataLoadAsync(
                dataType: "Catalog Sales",
                source: "Flexi ERP",
                recordCount: salesRecords.Count,
                success: true,
                parameters: parameters,
                duration: duration);

            return salesRecords;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            await _auditService.LogDataLoadAsync(
                dataType: "Catalog Sales",
                source: "Flexi ERP",
                recordCount: 0,
                success: false,
                parameters: parameters,
                errorMessage: ex.Message,
                duration: duration);
            throw;
        }
    }
}