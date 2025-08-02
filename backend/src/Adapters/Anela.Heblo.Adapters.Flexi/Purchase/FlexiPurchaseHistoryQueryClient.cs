using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Xcc.Audit;
using AutoMapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Client.Clients.UserQueries;
using Rem.FlexiBeeSDK.Client.ResultFilters;

namespace Anela.Heblo.Adapters.Flexi.Purchase;

public class FlexiPurchaseHistoryQueryClient : UserQueryClient<PurchaseHistoryFlexiDto>, IPurchaseHistoryClient
{
    private readonly IMemoryCache _cache;
    private readonly IMapper _mapper;
    private readonly IDataLoadAuditService _auditService;

    public FlexiPurchaseHistoryQueryClient(
        FlexiBeeSettings connection,
        IHttpClientFactory httpClientFactory,
        IResultHandler resultHandler,
        ILogger<ReceivedInvoiceClient> logger,
        IMemoryCache cache,
        IMapper mapper,
        IDataLoadAuditService auditService)
        : base(connection, httpClientFactory, resultHandler, logger)
    {
        _cache = cache;
        _mapper = mapper;
        _auditService = auditService;
    }

    protected override int QueryId => 19;

    public Task<IList<PurchaseHistoryFlexiDto>> GetAsync(string? productCode, DateTime dateFrom, DateTime dateTo, int limit = 0,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string>()
        {
            { "DATUM_OD", dateFrom.ToString("yyyy-MM-dd") },
            { "DATUM_DO", dateTo.ToString("yyyy-MM-dd") },
            { LimitParamName, limit.ToString() }
        };

        if (productCode != null)
        {
            query.Add("PRODUCT", productCode);
        }

        return GetAsync(query, cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogPurchaseRecord>> GetHistoryAsync(string? productCode, DateTime dateFrom, DateTime dateTo, int limit = 0, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var parameters = new Dictionary<string, object>
        {
            ["productCode"] = productCode ?? "all",
            ["dateFrom"] = dateFrom.ToString("yyyy-MM-dd"),
            ["dateTo"] = dateTo.ToString("yyyy-MM-dd"),
            ["limit"] = limit
        };

        if (!_cache.TryGetValue(GetKey(dateFrom, dateTo, limit), out IList<PurchaseHistoryFlexiDto>? purchaseHistory))
        {
            try
            {
                purchaseHistory = await GetAsync(null, dateFrom, dateTo, limit, cancellationToken);
                _cache.Set(GetKey(dateFrom, dateTo, limit), purchaseHistory, DateTimeOffset.Now.AddHours(1));

                var duration = DateTime.UtcNow - startTime;
                await _auditService.LogDataLoadAsync(
                    dataType: "Purchase History",
                    source: "Flexi ERP",
                    recordCount: purchaseHistory.Count,
                    success: true,
                    parameters: parameters,
                    duration: duration);
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                await _auditService.LogDataLoadAsync(
                    dataType: "Purchase History",
                    source: "Flexi ERP",
                    recordCount: 0,
                    success: false,
                    parameters: parameters,
                    errorMessage: ex.Message,
                    duration: duration);
                throw;
            }
        }

        return _mapper.Map<IReadOnlyList<CatalogPurchaseRecord>>(purchaseHistory!.Where(w => productCode == null || w.ProductCode == productCode).ToList());
    }

    private string GetKey(DateTime dateFrom, DateTime dateTo, int limit) =>
        $"PurchaseHistory_{dateFrom:yyyy-MM-dd}_{dateTo:yyyy-MM-dd}_{limit}";
}