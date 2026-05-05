using Anela.Heblo.Domain.Features.Catalog.Price;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Client.Clients.UserQueries;
using Rem.FlexiBeeSDK.Client.ResultFilters;

namespace Anela.Heblo.Adapters.Flexi.Price;

public class FlexiProductPriceErpClient : UserQueryClient<ProductPriceFlexiDto>, IProductPriceErpClient
{
    private readonly IMemoryCache _cache;
    private readonly IBoMClient _bomClient;
    private readonly ILogger<FlexiProductPriceErpClient> _clientLogger;
    private const string CacheKey = "FlexiProductPrices";

    public FlexiProductPriceErpClient(
        FlexiBeeSettings connection,
        IHttpClientFactory httpClientFactory,
        IResultHandler resultHandler,
        IMemoryCache cache,
        ILogger<ReceivedInvoiceClient> logger,
        IBoMClient bomClient,
        ILogger<FlexiProductPriceErpClient> clientLogger
    )
        : base(connection, httpClientFactory, resultHandler, logger)
    {
        _cache = cache;
        _bomClient = bomClient;
        _clientLogger = clientLogger;
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
        IList<ProductPriceFlexiDto>? data = null;

        // Safe cache access with disposed object protection
        try
        {
            if (!_cache.TryGetValue(CacheKey, out data))
            {
                // Cache miss - load from source
                data = null;
            }
        }
        catch (ObjectDisposedException)
        {
            // Cache is disposed, skip caching and load from source
            data = null;
        }

        if (data == null)
        {
            try
            {
                data = await GetAsync(0, cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _clientLogger.LogWarning(ex,
                    "FlexiBee uzivatelsky-dotaz/41 request timed out (internal HttpClient timeout).");
                throw;
            }
            catch (OperationCanceledException)
            {
                _clientLogger.LogInformation(
                    "FlexiBee uzivatelsky-dotaz/41 request was canceled by the caller (client abort).");
                throw;
            }

            // Safe cache set with disposed object protection
            try
            {
                _cache.Set(CacheKey, data, DateTimeOffset.UtcNow.AddMinutes(5));
            }
            catch (ObjectDisposedException)
            {
                // Cache is disposed, skip caching but continue with the data
            }

            dataLoaded = true;
        }

        var prices = data!.Select(s => new ProductPriceErp()
        {
            ProductCode = s.ProductCode,
            PriceWithoutVat = s.Price,
            PriceWithVat = s.Price * ((100 + s.Vat) / 100),
            PurchasePrice = s.PurchasePrice,
            PurchasePriceWithVat = s.PurchasePrice * ((100 + s.Vat) / 100),
            BoMId = s.BoMId
        }).ToList();

        return prices;
    }

    public async Task RecalculatePurchasePrice(int bomId, CancellationToken cancellationToken)
    {
        try
        {
            // Call the IBoMClient to recalculate purchase price for the specified BoM ID
            await _bomClient.RecalculatePurchasePrice(bomId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Log the error and re-throw
            throw new InvalidOperationException($"Failed to recalculate purchase price for BoM ID {bomId}: {ex.Message}", ex);
        }
    }
}