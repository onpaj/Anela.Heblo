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
/// Writes a Flexi ceník item's purchase price (<c>nakupCena</c>, excluding VAT, per <c>mj1</c>).
///
/// Used by the nightly purchase price sync to set materials and goods to their average stock
/// price, so Flexi's BoM roll-up (<c>prepocti-nakupni-cenu</c>) sums real values.
///
/// Addressed by the internal numeric id only: Flexi does not distinguish create from
/// update, so a PUT to <c>cenik/code:UNKNOWN.json</c> silently creates a new item.
/// </summary>
public class FlexiPurchasePriceWriter : IErpPurchasePriceWriter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FlexiBeeSettings _connection;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FlexiPurchasePriceWriter> _logger;

    public FlexiPurchasePriceWriter(
        IHttpClientFactory httpClientFactory,
        FlexiBeeSettings connection,
        IMemoryCache cache,
        ILogger<FlexiPurchasePriceWriter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _connection = connection;
        _cache = cache;
        _logger = logger;
    }

    public async Task SetPurchasePriceAsync(int erpItemId, decimal purchasePrice, CancellationToken ct)
    {
        if (erpItemId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(erpItemId),
                "A Flexi ceník id is required. Writing by code would create a new price list item.");
        }

        if (purchasePrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(purchasePrice),
                purchasePrice,
                "A Flexi ceník purchase price must be positive.");
        }

        var payload = new
        {
            winstrom = new
            {
                cenik = new
                {
                    nakupCena = purchasePrice.ToString("0.######", CultureInfo.InvariantCulture),
                },
            },
        };

        var url = $"{_connection.Server.TrimEnd('/')}/c/{_connection.Company}/cenik/{erpItemId}.json";

        // Same client naming and 5-minute timeout convention as FlexiProductPriceWriter.
        using var client = _httpClientFactory.CreateClient(nameof(FlexiPurchasePriceWriter));
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
                $"Flexi ceník purchase price write failed for id {erpItemId} with {(int)response.StatusCode}: {body}");
        }

        InvalidateCachedErpPrices();

        _logger.LogInformation(
            "Updated Flexi ceník {ErpItemId} purchase price to {PurchasePrice}", erpItemId, purchasePrice);
    }

    private void InvalidateCachedErpPrices()
    {
        try
        {
            _cache.Remove(FlexiProductPriceErpClient.CacheKey);
        }
        catch (ObjectDisposedException)
        {
            // A disposed cache is not a reason to report a completed live write as a failure.
        }
    }
}
