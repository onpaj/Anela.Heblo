using Anela.Heblo.Catalog.Purchase;
using Anela.Heblo.Data;
using Anela.Heblo.Purchase;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Client.Clients.UserQueries;
using Rem.FlexiBeeSDK.Client.ResultFilters;

namespace Anela.Heblo.Adapters.Flexi.Purchase;

public class FlexiPurchaseHistoryQueryClient : UserQueryClient<PurchaseHistory>, IPurchaseHistoryClient
{
    private readonly ISynchronizationContext _synchronizationContext;
    private readonly IMemoryCache _cache;

    public FlexiPurchaseHistoryQueryClient(
        FlexiBeeSettings connection, 
        IHttpClientFactory httpClientFactory, 
        IResultHandler resultHandler, 
        ILogger<ReceivedInvoiceClient> logger, 
        ISynchronizationContext synchronizationContext,
        IMemoryCache cache) 
        : base(connection, httpClientFactory, resultHandler, logger)
    {
        _synchronizationContext = synchronizationContext;
        _cache = cache;
    }

    protected override int QueryId => 19;

    public Task<IList<PurchaseHistory>> GetAsync(string? productCode, DateTime dateFrom, DateTime dateTo, int limit = 0,
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

    public async Task<IReadOnlyList<PurchaseHistoryData>> GetHistoryAsync(string? productCode, DateTime dateFrom, DateTime dateTo, int limit = 0, CancellationToken cancellationToken = default)
    {
        if (!_cache.TryGetValue(GetKey(dateFrom, dateTo, limit), out IList<PurchaseHistory>? purchaseHistory))
        {
            purchaseHistory = await GetAsync(null, dateFrom, dateTo, limit, cancellationToken);
            _synchronizationContext.Submit(new PurchaseHistorySyncData(purchaseHistory));
            _cache.Set(GetKey(dateFrom, dateTo, limit), purchaseHistory, DateTimeOffset.Now.AddHours(1));
        }

        return purchaseHistory!
            .Where(w=> productCode == null || w.ProductCode == productCode)
            .Select(p => new PurchaseHistoryData()
            {
                ProductCode = p.ProductCode,
                SupplierName = p.CompanyName,
                SupplierId = p.CompanyId,
                PricePerPiece = p.Price,
                PriceTotal = p.Price * (decimal)p.Amount,
                Amount = p.Amount,
                Date = p.Date,
                DocumentNumber = p.PurchaseDocumentNo,
            }).ToList();
    }

    private string GetKey(DateTime dateFrom, DateTime dateTo, int limit) =>
        $"PurchaseHistory_{dateFrom:yyyy-MM-dd}_{dateTo:yyyy-MM-dd}_{limit}";
}