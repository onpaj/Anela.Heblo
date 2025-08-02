using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Xcc.Audit;
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
    private readonly IDataLoadAuditService _auditService;
    private const string CacheKey = "FlexiProductPrices";

    public FlexiProductPriceErpClient(
        FlexiBeeSettings connection,
        IHttpClientFactory httpClientFactory,
        IResultHandler resultHandler,
        IMemoryCache cache,
        ILogger<ReceivedInvoiceClient> logger,
        IDataLoadAuditService auditService
    )
        : base(connection, httpClientFactory, resultHandler, logger)
    {
        _cache = cache;
        _auditService = auditService;
    }

    protected override int QueryId => 41;

    public Task<IList<ProductPriceFlexiDto>> GetAsync(int limit = 0, CancellationToken cancellationToken = default) =>
        GetAsync(new Dictionary<string, string>() { { LimitParamName, limit.ToString() } }, cancellationToken);


    public async Task<IEnumerable<ProductPriceErp>> GetAllAsync(bool forceReload, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var parameters = new Dictionary<string, object>
        {
            ["forceReload"] = forceReload,
            ["cacheKey"] = CacheKey
        };

        bool dataLoaded = false;
        if (!_cache.TryGetValue(CacheKey, out IList<ProductPriceFlexiDto>? data))
        {
            try
            {
                data = await GetAsync(0, cancellationToken);
                _cache.Set(CacheKey, data, DateTimeOffset.UtcNow.AddMinutes(5));
                dataLoaded = true;
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                await _auditService.LogDataLoadAsync(
                    dataType: "Product Prices",
                    source: "Flexi ERP",
                    recordCount: 0,
                    success: false,
                    parameters: parameters,
                    errorMessage: ex.Message,
                    duration: duration);
                throw;
            }
        }

        var prices = data!.Select(s => new ProductPriceErp()
        {
            ProductCode = s.ProductCode,
            PriceWithoutVat = s.Price,
            PriceWithVat = s.Price * ((100 + s.Vat) / 100),
            PurchasePrice = s.PurchasePrice,
            PurchasePriceWithVat = s.PurchasePrice * ((100 + s.Vat) / 100)
        }).ToList();

        if (dataLoaded)
        {
            var duration = DateTime.UtcNow - startTime;
            await _auditService.LogDataLoadAsync(
                dataType: "Product Prices",
                source: "Flexi ERP",
                recordCount: prices.Count(),
                success: true,
                parameters: parameters,
                duration: duration);
        }

        return prices;
    }
}