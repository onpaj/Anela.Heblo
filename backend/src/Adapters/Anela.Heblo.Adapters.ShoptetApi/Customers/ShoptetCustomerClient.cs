using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Customers.Model;
using Anela.Heblo.Application.Features.ShoptetCustomers;

namespace Anela.Heblo.Adapters.ShoptetApi.Customers;

public class ShoptetCustomerClient : IShoptetCustomerClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ShoptetCustomerClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ShoptetCustomerInfoDto?> GetCustomerByGuidAsync(string guid, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/customers/{guid}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"GET /api/customers/{guid} returned {(int)response.StatusCode}: {body}");
        }

        var data = await response.Content.ReadFromJsonAsync<ShoptetCustomerResponse>(JsonOptions, ct);
        var detail = data?.Data?.Customer;

        if (detail is null)
            return null;

        return new ShoptetCustomerInfoDto
        {
            Guid = detail.Guid,
            FullName = detail.FullName,
            Email = detail.Email,
            CustomerGroup = detail.CustomerGroup?.Name,
            PriceList = detail.PriceList?.Name,
            DefaultShippingAddress = FormatAddress(detail.BillingAddress),
        };
    }

    private static string? FormatAddress(ShoptetCustomerAddress? addr)
    {
        if (addr is null) return null;

        var parts = new[] { addr.CountryCode, addr.City, addr.Zip, addr.Street }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var joined = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }
}
