using Anela.Heblo.Application.Domain.Catalog.PurchaseHistory;
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

    public FlexiPurchaseHistoryQueryClient(
        FlexiBeeSettings connection, 
        IHttpClientFactory httpClientFactory, 
        IResultHandler resultHandler, 
        ILogger<ReceivedInvoiceClient> logger, 
        IMemoryCache cache) 
        : base(connection, httpClientFactory, resultHandler, logger)
    {
        _cache = cache;
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

        return purchaseHistory!
            .Where(w=> productCode == null || w.ProductCode == productCode)
            .Select(p => new CatalogPurchaseRecord()
            {
                ProductCode = p.ProductCode,
                SupplierName = p.CompanyName,
                SupplierId = p.CompanyId,
                PricePerPiece = p.Price,
                PriceTotal = p.Price * (decimal)p.Amount,
                Amount = p.Amount,
                Date = DateTime.SpecifyKind(p.Date, DateTimeKind.Unspecified),
                DocumentNumber = p.PurchaseDocumentNo,
            }).ToList();
    }

    private string GetKey(DateTime dateFrom, DateTime dateTo, int limit) =>
        $"PurchaseHistory_{dateFrom:yyyy-MM-dd}_{dateTo:yyyy-MM-dd}_{limit}";
}