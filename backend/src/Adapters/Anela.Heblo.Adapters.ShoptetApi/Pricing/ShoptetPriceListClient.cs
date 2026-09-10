using System.Globalization;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Pricing.Model;
using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Pricing;

public class ShoptetPriceListClient : IEshopPriceListClient
{
    /// <summary>Shoptet caps the price list detail page size at 100.</summary>
    private const int MaxItemsPerPage = 100;

    private readonly HttpClient _httpClient;
    private readonly IOptions<ShoptetApiSettings> _settings;
    private readonly ILogger<ShoptetPriceListClient> _logger;

    public ShoptetPriceListClient(
        HttpClient httpClient,
        IOptions<ShoptetApiSettings> settings,
        ILogger<ShoptetPriceListClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetPricesWithVatAsync(CancellationToken ct)
    {
        var priceListId = ResolvePriceListId();
        var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var unreadableCount = 0;

        var page = 1;
        int pageCount;
        do
        {
            var url = $"/api/pricelists/{priceListId}?itemsPerPage={MaxItemsPerPage}&page={page}";
            var snapshot = await GetAsync<PriceListSnapshotResponse>(url, ct);

            // A 200 whose body carries no `data` block is a malformed response, not an empty
            // price list. Swallowing it would hand the sync an empty snapshot, and every
            // in-scope product would then decide MissingRemote and be marked Failed in a
            // single run — the exact mass-failure the caller's try/catch exists to prevent.
            var data = snapshot.Data
                ?? throw new HttpRequestException($"Shoptet returned no data block for {url}");

            foreach (var item in data.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Code))
                {
                    continue;
                }

                var rawPrice = item.Price?.Price;
                if (rawPrice is null)
                {
                    // Legitimate: the product has no price set in this list.
                    continue;
                }

                if (!TryComputePriceWithVat(item, rawPrice, out var priceWithVat))
                {
                    unreadableCount++;
                    _logger.LogError(
                        "Shoptet price list {PriceListId}: could not interpret the price for product " +
                        "{Code} (price={Price}, includingVat={IncludingVat}, vatRate={VatRate}); skipping it.",
                        priceListId, item.Code, rawPrice, item.IncludingVat, item.VatRate);
                    continue;
                }

                prices[item.Code] = priceWithVat;
            }

            pageCount = data.Paginator?.PageCount ?? 1;
            page++;
        }
        while (page <= pageCount);

        if (unreadableCount > 0)
        {
            _logger.LogWarning(
                "Shoptet price list {PriceListId}: {Count} item(s) had a price that could not be " +
                "interpreted and were skipped.",
                priceListId, unreadableCount);
        }

        _logger.LogInformation("Read {Count} prices from Shoptet price list {PriceListId}", prices.Count, priceListId);
        return prices;
    }

    public async Task<decimal?> GetPriceWithVatAsync(string productCode, CancellationToken ct)
    {
        var priceListId = ResolvePriceListId();

        // `code=` (singular) is supported and returns totalCount 1; `codes=` is rejected outright.
        var url = $"/api/pricelists/{priceListId}?code={Uri.EscapeDataString(productCode)}";
        var snapshot = await GetAsync<PriceListSnapshotResponse>(url, ct);

        var data = snapshot.Data
            ?? throw new HttpRequestException($"Shoptet returned no data block for {url}");

        var item = data.Items.FirstOrDefault(i =>
            string.Equals(i.Code, productCode, StringComparison.OrdinalIgnoreCase));

        var rawPrice = item?.Price?.Price;
        if (item is null || rawPrice is null)
        {
            return null;
        }

        return TryComputePriceWithVat(item, rawPrice, out var priceWithVat) ? priceWithVat : null;
    }

    public async Task SetPriceWithVatAsync(string productCode, decimal priceWithVat, CancellationToken ct)
    {
        var priceListId = ResolvePriceListId();

        // priceWithVat (never `price`) so Shoptet recalculates the stored form itself.
        // It is an object group with the same members as the read-side `price`
        // (price/commonPrice/buyPrice/priceRatio/actionPrice), NOT a scalar: sending the
        // amount flat returns 422 `invalid-request-data` — "String value found, but an
        // object is required" at `data[0].priceWithVat`. Only `price` is sent, so the
        // other members stay untouched. The schema is `additionalProperties: false` and
        // requires exactly 2 decimals (`^(-)?[0-9]+\.[0-9]{2}$`), which F2 satisfies.
        // Never send 0 to mean "no price" — from 2026-09-14 that is a genuine zero price.
        var payload = new
        {
            data = new[]
            {
                new
                {
                    code = productCode,
                    priceWithVat = new
                    {
                        price = priceWithVat.ToString("F2", CultureInfo.InvariantCulture),
                    },
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/pricelists/{priceListId}")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <summary>
    /// <c>GET /api/pricelists</c> returns no <c>default</c> flag (each entry is only
    /// <c>{id, name}</c>), so the retail list cannot be discovered automatically. It must be
    /// configured explicitly.
    /// </summary>
    private int ResolvePriceListId() =>
        _settings.Value.DefaultPriceListId
            ?? throw new InvalidOperationException(
                "Shoptet:DefaultPriceListId is not configured. GET /api/pricelists returns no " +
                "default-list flag, so the retail price list cannot be resolved automatically — " +
                "set Shoptet:DefaultPriceListId explicitly (the Anela retail list is id 1, \"Hlavní ceník\").");

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, ct);
        await EnsureSuccessAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
            ?? throw new HttpRequestException($"Shoptet returned an empty body for {url}");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"Shoptet price list request failed with {(int)response.StatusCode}: {body}");
    }

    /// <summary>
    /// Derives the with-VAT price from the item's own <c>includingVat</c> flag rather than
    /// assuming either value: when true, <c>price.price</c> already is the with-VAT price;
    /// when false, it must be grossed up using the item's <c>vatRate</c>.
    /// </summary>
    private static bool TryComputePriceWithVat(PriceListSnapshotItem item, string rawPrice, out decimal priceWithVat)
    {
        priceWithVat = 0m;

        if (!decimal.TryParse(rawPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedPrice))
        {
            return false;
        }

        if (item.IncludingVat)
        {
            priceWithVat = Math.Round(parsedPrice, 2, MidpointRounding.AwayFromZero);
            return true;
        }

        if (!decimal.TryParse(item.VatRate, NumberStyles.Number, CultureInfo.InvariantCulture, out var vatRate))
        {
            return false;
        }

        priceWithVat = Math.Round(parsedPrice * (1 + vatRate / 100m), 2, MidpointRounding.AwayFromZero);
        return true;
    }
}
