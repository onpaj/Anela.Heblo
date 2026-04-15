using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;

public class ShoptetInvoiceClient : IShoptetInvoiceClient
{
    private const int DefaultItemsPerPage = 50;

    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ShoptetInvoiceClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<ShoptetInvoiceDto>> ListInvoicesAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        CancellationToken ct = default)
    {
        var result = new List<ShoptetInvoiceDto>();
        var page = 1;

        while (true)
        {
            var url = BuildListUrl(dateFrom, dateTo, page);
            var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<InvoiceListResponse>(JsonOptions, ct);
            if (data == null)
                break;

            result.AddRange(data.Data.Invoices);

            if (data.Data.Paginator.Page >= data.Data.Paginator.PageCount)
                break;

            page++;
        }

        return result;
    }

    public async Task<ShoptetInvoiceDto?> GetInvoiceAsync(string code, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/invoices/{Uri.EscapeDataString(code)}", ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<InvoiceDetailResponse>(JsonOptions, ct);
        return data?.Data?.Invoice;
    }

    private static string BuildListUrl(DateTime? dateFrom, DateTime? dateTo, int page)
    {
        var from = (dateFrom ?? DateTime.UtcNow.AddDays(-1)).ToString("s") + "Z";
        var to = (dateTo ?? DateTime.UtcNow).ToString("s") + "Z";
        return $"/api/invoices?creationTimeFrom={Uri.EscapeDataString(from)}&creationTimeTo={Uri.EscapeDataString(to)}&page={page}&itemsPerPage={DefaultItemsPerPage}";
    }
}
