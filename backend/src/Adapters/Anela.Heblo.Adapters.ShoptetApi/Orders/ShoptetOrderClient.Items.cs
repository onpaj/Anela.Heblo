using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Application.Features.ShoptetOrders;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

/// <summary>
/// Order-item operations (add / delete / read lines). Behaviour verified on the test store and
/// documented in docs/integrations/shoptet-api.md §3.17. PATCH of an item is deliberately absent:
/// changing a line's code only relabels it and corrupts stock.
/// </summary>
public partial class ShoptetOrderClient
{
    private const int OrderListPageSize = 50;
    private const string CreationTimeFormat = "yyyy-MM-dd'T'HH:mm:sszzz";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public async Task<IReadOnlyList<string>> ListOrderCodesContainingProductAsync(
        string productCode, DateTimeOffset createdFrom, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);

        var code = Uri.EscapeDataString(productCode);
        var from = Uri.EscapeDataString(createdFrom.ToString(CreationTimeFormat, Invariant));
        var result = new List<string>();

        for (var page = 1; ; page++)
        {
            var path = $"/api/orders?productCode={code}&creationTimeFrom={from}&page={page}&itemsPerPage={OrderListPageSize}";
            var response = await _http.GetAsync(path, ct);
            await EnsureShoptetSuccessAsync(response, "GET", path, ct);

            var data = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
            if (data == null)
                break;

            result.AddRange(data.Data.Orders.Select(o => o.Code));
            if (page >= data.Data.Paginator.PageCount)
                break;
        }

        return result;
    }

    public async Task<EshopOrderDetail?> GetOrderDetailAsync(string orderCode, CancellationToken ct = default)
    {
        var path = $"/api/orders/{Uri.EscapeDataString(orderCode)}";
        var response = await _http.GetAsync(path, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureShoptetSuccessAsync(response, "GET", path, ct);

        var data = await response.Content.ReadFromJsonAsync<OrderItemsDetailResponse>(JsonOptions, ct);
        var order = data?.Data?.Order;
        if (order == null)
            return null;

        return new EshopOrderDetail
        {
            Code = order.Code,
            StatusId = order.Status.Id,
            Lines = order.Items.Select(MapLine).ToList(),
        };
    }

    public async Task AddOrderItemAsync(string orderCode, NewEshopOrderItem item, CancellationToken ct = default)
    {
        var path = $"/api/orders/{Uri.EscapeDataString(orderCode)}/item";
        var body = new AddOrderItemRequest
        {
            Data = new AddOrderItemData
            {
                ItemType = item.ItemType,
                Code = item.Code,
                Amount = item.Amount.ToString("0.###", Invariant),
                UnitPriceWithVat = item.UnitPriceWithVat.ToString("0.00", Invariant),
                VatRate = item.VatRate.ToString("0.##", Invariant),
                PriceRatio = item.PriceRatio.ToString("0.0000", Invariant),
                Remark = item.Remark,
            },
        };

        var response = await _http.PostAsJsonAsync(path, body, JsonOptions, ct);
        await EnsureShoptetSuccessAsync(response, "POST", path, ct);
    }

    public async Task DeleteOrderItemAsync(string orderCode, long itemId, CancellationToken ct = default)
    {
        var path = $"/api/orders/{Uri.EscapeDataString(orderCode)}/item/{itemId.ToString(Invariant)}";
        var response = await _http.DeleteAsync(path, ct);
        await EnsureShoptetSuccessAsync(response, "DELETE", path, ct);
    }

    private static EshopOrderLine MapLine(ShoptetOrderLine line) =>
        new()
        {
            ItemId = line.ItemId,
            ItemType = line.ItemType,
            Code = line.Code,
            Amount = ParseDecimal(line.Amount, 0m),
            UnitPriceWithVat = ParseDecimal(line.UnitPrice?.WithVat, 0m),
            VatRate = ParseDecimal(line.UnitPrice?.VatRate, 0m),
            PriceRatio = ParseDecimal(line.PriceRatio, 1m),
            Remark = line.Remark,
        };

    private static decimal ParseDecimal(string? value, decimal fallback) =>
        decimal.TryParse(value, NumberStyles.Number, Invariant, out var parsed) ? parsed : fallback;

    /// <summary>
    /// Like EnsureSuccessStatusCode, but keeps Shoptet's errors[] (errorCode + message) in the
    /// exception text so a 422 explains itself in the logs. StatusCode stays set for callers.
    /// </summary>
    private static async Task EnsureShoptetSuccessAsync(
        HttpResponseMessage response, string method, string path, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"Shoptet {method} {path} returned {(int)response.StatusCode}: {DescribeShoptetErrors(body)}",
            inner: null,
            statusCode: response.StatusCode);
    }

    private static string DescribeShoptetErrors(string body)
    {
        try
        {
            var errors = JsonSerializer.Deserialize<ShoptetErrorResponse>(body, JsonOptions)?.Errors;
            if (errors is { Count: > 0 })
                return string.Join("; ", errors.Select(e => $"{e.ErrorCode} – {e.Message}"));
        }
        catch (JsonException)
        {
            // Not Shoptet's JSON envelope (e.g. a gateway HTML page) — fall through to the raw body.
        }

        return body;
    }
}
