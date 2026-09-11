using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;

namespace Anela.Heblo.Adapters.Flexi.Price;

/// <summary>
/// Writes a Flexi ceník item's selling price, INCLUDING VAT.
///
/// Flexi interprets <c>cenaZakl</c> through the item's own <c>typCenyDphK</c>: for a
/// <c>bezDph</c> item it is the price excluding VAT and Flexi grosses it up on read. So the
/// price and the flag are written TOGETHER — <c>typCeny.sDph</c> declares "this number
/// includes VAT", which makes the write self-describing instead of dependent on how the item
/// happened to be configured, and removes any need for a VAT rate.
///
/// Verified against the live ERP 2026-09-11: writing cenaZakl alone put 287 in as a base
/// price on a bezDph item, which Flexi then showed as 347.27 including VAT.
///
/// Addressed by the internal numeric id only: Flexi does not distinguish create from
/// update, so a PUT to <c>cenik/code:UNKNOWN.json</c> silently creates a new item.
/// </summary>
public class FlexiProductPriceWriter : IErpPriceWriter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FlexiBeeSettings _connection;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FlexiProductPriceWriter> _logger;

    public FlexiProductPriceWriter(
        IHttpClientFactory httpClientFactory,
        FlexiBeeSettings connection,
        IMemoryCache cache,
        ILogger<FlexiProductPriceWriter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _connection = connection;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Flexi's enum value for "price is entered including VAT".</summary>
    private const string IncludingVatPriceType = "typCeny.sDph";

    public async Task SetPriceWithVatAsync(int erpItemId, decimal priceWithVat, CancellationToken ct)
    {
        if (erpItemId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(erpItemId),
                "A Flexi ceník id is required. Writing by code would create a new price list item.");
        }

        // Same reason the id is guarded: this writer is reachable by any future caller that
        // has not been through SetProductPriceRequestValidator, and a zero or negative
        // cenaZakl lands in a live ERP.
        if (priceWithVat <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(priceWithVat),
                priceWithVat,
                "A Flexi ceník base price must be positive.");
        }

        var payload = new
        {
            winstrom = new
            {
                cenik = new
                {
                    cenaZakl = priceWithVat.ToString("F2", CultureInfo.InvariantCulture),
                    // Declares what cenaZakl above means. Without it Flexi falls back to the
                    // item's existing price type and a "bezDph" item grosses the number up.
                    typCenyDphK = IncludingVatPriceType,
                },
            },
        };

        var url = $"{_connection.Server.TrimEnd('/')}/c/{_connection.Company}/cenik/{erpItemId}.json";

        // Named the same way every Rem.FlexiBeeSDK ResourceClient names its client (its own
        // type name via IHttpClientFactory) and given the same 5-minute timeout that
        // ResourceClient.GetClient() applies to every other synchronous Flexi call, instead
        // of an unnamed client stuck on the default 100s timeout. Flexi has no adapter-wide
        // Polly policy registered on its HttpClient (the one circuit breaker in this codebase,
        // ManufactureErpResilienceService, is scoped to the manufacture-submit call only), so
        // matching this timeout convention is what "the adapter's configured client" means here.
        using var client = _httpClientFactory.CreateClient(nameof(FlexiProductPriceWriter));
        client.Timeout = TimeSpan.FromMinutes(5);
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_connection.Login}:{_connection.Password}")));

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Flexi ceník write failed for id {erpItemId} with {(int)response.StatusCode}: {body}");
        }

        // Flexi now holds a price the cached ceník read does not. Left in place, the very
        // next comparison — the one the frontend triggers right after a successful save —
        // would read Shoptet live (new price) and Flexi from a cache entry up to 5 minutes
        // old (previous price), and render a change that fully succeeded as a FlexiDiffers
        // divergence, on the screen that exists to surface real divergence.
        InvalidateCachedErpPrices();

        _logger.LogInformation(
            "Updated Flexi ceník {ErpItemId} base price to {Price}", erpItemId, priceWithVat);
    }

    private void InvalidateCachedErpPrices()
    {
        try
        {
            _cache.Remove(FlexiProductPriceErpClient.CacheKey);
        }
        catch (ObjectDisposedException)
        {
            // Same accommodation FlexiProductPriceErpClient makes: a disposed cache is not a
            // reason to report a completed live price write as a failure.
        }
    }
}
