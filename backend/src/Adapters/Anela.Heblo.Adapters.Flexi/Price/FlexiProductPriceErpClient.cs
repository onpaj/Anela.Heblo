using Anela.Heblo.Data;
using Anela.Heblo.Price;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Client.Clients.UserQueries;
using Rem.FlexiBeeSDK.Client.ResultFilters;

namespace Anela.Heblo.Adapters.Flexi.Price;

public class FlexiProductPriceErpClient : UserQueryClient<ProductPriceFlexiDto>, IProductPriceErpClient
{
    private readonly IMemoryCache _cache;
    private readonly ISynchronizationContext _synchronizationContext;
    private const string CacheKey = "FlexiProductPrices";

    public FlexiProductPriceErpClient(
        FlexiBeeSettings connection, 
        IHttpClientFactory httpClientFactory, 
        IResultHandler resultHandler, 
        IMemoryCache cache, 
        ISynchronizationContext synchronizationContext,
        ILogger<ReceivedInvoiceClient> logger
    ) 
        : base(connection, httpClientFactory, resultHandler, logger)
    {
        _cache = cache;
        _synchronizationContext = synchronizationContext;
    }

    protected override int QueryId => 41;
    
    public Task<IList<ProductPriceFlexiDto>> GetAsync(int limit = 0, CancellationToken cancellationToken = default) =>
        GetAsync(new Dictionary<string, string>() { { LimitParamName, limit.ToString() } }, cancellationToken);

    
    public async Task<IEnumerable<ProductPriceErp>> GetAllAsync(bool forceReload, CancellationToken cancellationToken)
    {
        bool dataLoaded = false;
        if (!_cache.TryGetValue(CacheKey, out IList<ProductPriceFlexiDto>? data))
        {
            data = await GetAsync(0, cancellationToken);
            _cache.Set(CacheKey, data, DateTime.Now.AddMinutes(5));
            dataLoaded = true;
        }
        
        var prices = data!.Select(s => new ProductPriceErp()
        {
            ProductCode = s.ProductCode,
            Price = s.Price,
            PriceWithVat = s.Price * ((100 + s.Vat) / 100),
            PurchasePrice = s.PurchasePrice,
            PurchasePriceWithVat = s.PurchasePrice * ((100 + s.Vat) / 100),
            BoMId = s.BoMId
        }).ToList();

        if (dataLoaded)
        {
            _synchronizationContext.Submit(new ProductPriceSyncData(prices));
        }

        return prices;
    }
}