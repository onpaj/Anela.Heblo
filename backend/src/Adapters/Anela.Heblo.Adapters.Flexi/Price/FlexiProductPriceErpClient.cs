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
    /// <summary>
    /// Cache key for the whole ceník read. Shared with <see cref="FlexiProductPriceWriter"/>,
    /// which evicts this entry after a successful price write so the next comparison does not
    /// render a change that fully succeeded as a divergence.
    /// </summary>
    internal const string CacheKey = "FlexiProductPrices";

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

        IList<ProductPriceFlexiDto>? data = null;

        // Safe cache access with disposed object protection. forceReload skips the lookup
        // entirely (and the fresh result below replaces the entry) — it used to be accepted
        // and silently dropped, so no caller could ever get a fresh read out of this client.
        if (!forceReload)
        {
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
        }

        return MapToProductPrices(data!).ToList();
    }

    /// <summary>
    /// Projects the raw Flexi rows to <see cref="ProductPriceErp"/>, deriving the with-VAT
    /// price from each item's own price-type flag instead of unconditionally grossing up
    /// (that double-counts VAT for an item entered "s DPH"). User query 41 may not expose
    /// <c>typCenyDphK</c> at all; when it is absent, excl-VAT semantics (today's behavior) is
    /// assumed and a warning is logged once for the whole batch — never silently.
    ///
    /// The same "warn once, never silently" treatment applies to an unrecognised VAT band
    /// (<c>typszbdphk</c>): the read path keeps its 21% fallback so the comparison screen is
    /// unchanged, but <see cref="ProductPriceErp.VatRate"/> is left null so the write path can
    /// refuse. The warning carries the raw value because nobody here can see query 41's
    /// definition — the first live run is how we learn which vocabulary it actually returns.
    /// </summary>
    internal IEnumerable<ProductPriceErp> MapToProductPrices(IEnumerable<ProductPriceFlexiDto> data)
    {
        var warnedAboutUnknownPriceType = false;
        var warnedAboutUnknownVatLevel = false;

        foreach (var s in data)
        {
            if (s.VatRate is null && !warnedAboutUnknownVatLevel)
            {
                _clientLogger.LogWarning(
                    "FlexiBee uzivatelsky-dotaz/41: unrecognised typszbdphk value '{VatLevel}' " +
                    "for one or more items (e.g. {ProductCode}); the read falls back to 21% but " +
                    "price writes for these items are refused.",
                    s.VatLevel, s.ProductCode);
                warnedAboutUnknownVatLevel = true;
            }

            if (s.TypCenyDphK is null && !warnedAboutUnknownPriceType)
            {
                _clientLogger.LogWarning(
                    "FlexiBee uzivatelsky-dotaz/41: typCenyDphK is missing for one or more items " +
                    "(e.g. {ProductCode}); assuming excl-VAT (bez DPH) semantics for cenaZakl.",
                    s.ProductCode);
                warnedAboutUnknownPriceType = true;
            }

            decimal priceWithVat;
            decimal priceWithoutVat;
            if (s.IsPriceIncludingVat)
            {
                priceWithVat = s.Price;
                priceWithoutVat = Math.Round(s.Price / (1 + s.Vat / 100m), 2, MidpointRounding.AwayFromZero);
            }
            else
            {
                priceWithoutVat = s.Price;
                priceWithVat = s.Price * ((100 + s.Vat) / 100);
            }

            yield return new ProductPriceErp
            {
                ProductCode = s.ProductCode,
                PriceWithoutVat = priceWithoutVat,
                PriceWithVat = priceWithVat,
                PurchasePrice = s.PurchasePrice,
                PurchasePriceWithVat = s.PurchasePrice * ((100 + s.Vat) / 100),
                BoMId = s.BoMId,
                ErpItemId = s.ProductId,
                VatRate = s.VatRate,
                ErpPriceType = s.TypCenyDphK switch
                {
                    "typCeny.bezDph" => "bezDph",
                    "typCeny.sDph" => "sDph",
                    _ => null
                }
            };
        }
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