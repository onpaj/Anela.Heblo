using Anela.Heblo.Application.Domain.Catalog.PurchaseHistory;
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

    public FlexiPurchaseHistoryQueryClient(
        FlexiBeeSettings connection,
        IHttpClientFactory httpClientFactory,
        IResultHandler resultHandler,
        ILogger<ReceivedInvoiceClient> logger,
        IMemoryCache cache,
        IMapper mapper)
        : base(connection, httpClientFactory, resultHandler, logger)
    {
        _cache = cache;
        _mapper = mapper;
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
        if (!_cache.TryGetValue(GetKey(dateFrom, dateTo, limit), out IList<PurchaseHistoryFlexiDto>? purchaseHistory))
        {
            purchaseHistory = await GetAsync(null, dateFrom, dateTo, limit, cancellationToken);
            // TODO Add Audit trace to log successful load
            _cache.Set(GetKey(dateFrom, dateTo, limit), purchaseHistory, DateTimeOffset.Now.AddHours(1));
        }

        return _mapper.Map<IReadOnlyList<CatalogPurchaseRecord>>(purchaseHistory!.Where(w => productCode == null || w.ProductCode == productCode).ToList());
    }

    private string GetKey(DateTime dateFrom, DateTime dateTo, int limit) =>
        $"PurchaseHistory_{dateFrom:yyyy-MM-dd}_{dateTo:yyyy-MM-dd}_{limit}";
}