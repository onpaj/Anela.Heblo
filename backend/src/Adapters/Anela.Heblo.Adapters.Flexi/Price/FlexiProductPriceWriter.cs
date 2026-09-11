using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;

namespace Anela.Heblo.Adapters.Flexi.Price;

/// <summary>
/// Writes <c>cenaZakl</c> (base price, excluding VAT) to a Flexi ceník item.
///
/// Addressed by the internal numeric id only: Flexi does not distinguish create from
/// update, so a PUT to <c>cenik/code:UNKNOWN.json</c> silently creates a new item.
/// </summary>
public class FlexiProductPriceWriter : IErpPriceWriter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FlexiBeeSettings _connection;
    private readonly ILogger<FlexiProductPriceWriter> _logger;

    public FlexiProductPriceWriter(
        IHttpClientFactory httpClientFactory,
        FlexiBeeSettings connection,
        ILogger<FlexiProductPriceWriter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _connection = connection;
        _logger = logger;
    }

    public async Task SetPriceWithoutVatAsync(int erpItemId, decimal priceWithoutVat, CancellationToken ct)
    {
        if (erpItemId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(erpItemId),
                "A Flexi ceník id is required. Writing by code would create a new price list item.");
        }

        var payload = new
        {
            winstrom = new
            {
                cenik = new
                {
                    cenaZakl = priceWithoutVat.ToString("F2", CultureInfo.InvariantCulture),
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

        _logger.LogInformation(
            "Updated Flexi ceník {ErpItemId} base price to {Price}", erpItemId, priceWithoutVat);
    }
}
