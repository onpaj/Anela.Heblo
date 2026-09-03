using System.Globalization;
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
    /// <summary>Shoptet caps the snapshot page size at 100.</summary>
    private const int MaxItemsPerPage = 100;

    private readonly HttpClient _httpClient;
    private readonly IOptions<ShoptetApiSettings> _settings;
    private readonly ILogger<ShoptetPriceListClient> _logger;
    private int? _resolvedPriceListId;

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
        var priceListId = await ResolvePriceListIdAsync(ct);
        var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        var page = 1;
        int pageCount;
        do
        {
            var url = $"/api/pricelists/{priceListId}/snapshot?itemsPerPage={MaxItemsPerPage}&page={page}";
            var snapshot = await GetAsync<PriceListSnapshotResponse>(url, ct);

            foreach (var item in snapshot.Data?.Items ?? new List<PriceListSnapshotItem>())
            {
                if (string.IsNullOrWhiteSpace(item.Code) || !TryParsePrice(item.PriceWithVat, out var price))
                {
                    continue;
                }

                prices[item.Code] = price;
            }

            pageCount = snapshot.Data?.Paginator?.PageCount ?? 1;
            page++;
        }
        while (page <= pageCount);

        _logger.LogInformation("Read {Count} prices from Shoptet price list {PriceListId}", prices.Count, priceListId);
        return prices;
    }

    public async Task SetPriceWithVatAsync(string productCode, decimal priceWithVat, CancellationToken ct)
    {
        var priceListId = await ResolvePriceListIdAsync(ct);

        // priceWithVat (never `price`) so Shoptet recalculates the stored form itself.
        // Never send 0 to mean "no price" — from 2026-09-14 that is a genuine zero price.
        var payload = new
        {
            data = new[]
            {
                new
                {
                    code = productCode,
                    priceWithVat = priceWithVat.ToString("F2", CultureInfo.InvariantCulture),
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

    private async Task<int> ResolvePriceListIdAsync(CancellationToken ct)
    {
        if (_settings.Value.DefaultPriceListId is { } configured)
        {
            return configured;
        }

        if (_resolvedPriceListId is { } cached)
        {
            return cached;
        }

        var lists = await GetAsync<PriceListResponse>("/api/pricelists", ct);
        var defaultList = lists.Data?.PriceLists.FirstOrDefault(l => l.IsDefault)
            ?? throw new InvalidOperationException(
                "Shoptet returned no default price list. Set Shoptet:DefaultPriceListId explicitly.");

        _resolvedPriceListId = defaultList.Id;
        return defaultList.Id;
    }

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

    private static bool TryParsePrice(string? raw, out decimal price) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out price);
}
