using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Application.Features.ShoptetOrders;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

public class ShoptetOrderClient : IEshopOrderClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const int AdditionalFieldMinIndex = 1;
    private const int AdditionalFieldMaxIndex = 6;
    private const int AdditionalFieldShortTextMaxIndex = 3;
    private const int AdditionalFieldShortTextMaxLength = 255;

    public ShoptetOrderClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<EshopOrderSummary>> GetRecentOrdersAsync(int count = 20, CancellationToken ct = default)
    {
        var itemsPerPage = Math.Min(count, 50);
        var response = await _http.GetAsync($"/api/orders?page=1&itemsPerPage={itemsPerPage}", ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
        return (data?.Data.Orders ?? [])
            .Take(count)
            .Select(o => new EshopOrderSummary
            {
                Code = o.Code,
                Email = o.Email,
                StatusId = o.Status.Id,
            })
            .ToList();
    }

    public async Task<List<EshopOrderSummary>> ListByExternalCodePrefixAsync(
        string prefix,
        string? emailFilter = null,
        CancellationToken ct = default)
    {
        var result = new List<EshopOrderSummary>();
        var page = 1;

        while (true)
        {
            var response = await _http.GetAsync($"/api/orders?page={page}&itemsPerPage=50", ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
            if (data == null)
                break;

            IEnumerable<OrderSummary> candidates = data.Data.Orders;

            if (emailFilter != null)
                candidates = candidates.Where(o => string.Equals(o.Email, emailFilter, StringComparison.OrdinalIgnoreCase));

            foreach (var summary in candidates)
            {
                var detail = await GetOrderDetailInternalAsync(summary.Code, ct);
                if (detail.ExternalCode?.StartsWith(prefix, StringComparison.Ordinal) == true)
                    result.Add(MapToSummary(detail));
            }

            if (page >= data.Data.Paginator.PageCount)
                break;

            page++;
        }

        return result;
    }

    public async Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default)
    {
        var detail = await GetOrderDetailInternalAsync(orderCode, ct);
        return detail.Status.Id;
    }

    public async Task<string> GetEshopRemarkAsync(string orderCode, CancellationToken ct = default)
    {
        // eshopRemark is inside the nested "notes" object which requires ?include=notes.
        // The existing GetOrderDetailInternalAsync calls GET /api/orders/{code} without this
        // include, so the notes object would be null. We make a dedicated call here.
        var response = await _http.GetAsync($"/api/orders/{orderCode}?include=notes", ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions, ct);
        return data?.Data?.Order?.Notes?.EshopRemark ?? string.Empty;
    }

    public async Task<string> CreateOrderAsync(CreateEshopOrderRequest request, CancellationToken ct = default)
    {
        var body = new ShoptetCreateOrderBody
        {
            Email = request.Email,
            Phone = request.Phone,
            ExternalCode = request.ExternalCode,
            ShippingGuid = request.ShippingGuid,
            PaymentMethodGuid = request.PaymentMethodGuid,
            Currency = new ShoptetCurrency { Code = request.CurrencyCode },
            BillingAddress = new ShoptetAddress
            {
                FullName = request.BillingAddress.FullName,
                Street = request.BillingAddress.Street,
                City = request.BillingAddress.City,
                Zip = request.BillingAddress.Zip,
            },
            Items = request.Items.Select(i => new ShoptetOrderItem
            {
                ItemType = i.ItemType,
                Code = i.Code,
                Name = i.Name,
                VatRate = i.VatRate,
                ItemPriceWithVat = i.ItemPriceWithVat,
                Amount = i.Amount,
            }).ToList(),
        };

        var envelope = new { data = body };
        var response = await _http.PostAsJsonAsync("/api/orders", envelope, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"POST /api/orders returned {(int)response.StatusCode}: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions, ct);
        return result!.Data.Order.Code;
    }

    public async Task UpdateStatusAsync(string orderCode, int statusId, CancellationToken ct = default)
    {
        var body = new UpdateStatusRequest
        {
            Data = new UpdateStatusData { StatusId = statusId },
        };

        var response = await _http.PatchAsJsonAsync($"/api/orders/{orderCode}/status", body, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"PATCH /api/orders/{orderCode}/status returned {(int)response.StatusCode}: {errorBody}");
        }
    }

    public async Task SetInternalNoteAsync(string orderCode, string note, CancellationToken ct = default)
    {
        var body = new CreateOrderRemarkRequest
        {
            Data = new CreateOrderRemarkData { Text = note, Type = "system" },
        };

        var response = await _http.PostAsJsonAsync($"/api/orders/{orderCode}/history", body, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"POST /api/orders/{orderCode}/history returned {(int)response.StatusCode}: {errorBody}");
        }
    }

    public async Task UpdateEshopRemarkAsync(string orderCode, string eshopRemark, CancellationToken ct = default)
    {
        var body = new UpdateEshopRemarkRequest
        {
            Data = new UpdateEshopRemarkData { EshopRemark = eshopRemark },
        };

        var response = await _http.PatchAsJsonAsync($"/api/orders/{orderCode}/notes", body, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"PATCH /api/orders/{orderCode}/notes returned {(int)response.StatusCode}: {errorBody}");
        }
    }

    public async Task DeleteOrderAsync(string orderCode, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/orders/{orderCode}", ct);
        response.EnsureSuccessStatusCode();
    }

    // NOTE: Fetches only page 1. For high-volume stores the matching orders may not appear on the first page,
    // which can cause the email-fallback resolution path to under-return results or miss the customer GUID.
    public async Task<List<EshopOrderInfo>> GetRecentOrdersByEmailAsync(string email, int count, CancellationToken ct = default)
    {
        var itemsPerPage = Math.Min(count, 50);
        var response = await _http.GetAsync($"/api/orders?page=1&itemsPerPage={itemsPerPage}", ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
        return (data?.Data.Orders ?? [])
            .Where(o => string.Equals(o.Email, email, StringComparison.OrdinalIgnoreCase))
            .Take(count)
            .Select(MapToOrderInfo)
            .ToList();
    }

    public async Task<Dictionary<int, string>> GetOrderStatusNamesAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/eshop?include=orderStatuses", ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<ShoptetEshopResponse>(JsonOptions, ct);
        return (data?.Data?.Eshop?.OrderStatuses ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .ToDictionary(s => s.Id, s => s.Name!);
    }

    // ── Expedition methods ────────────────────────────────────────────────────

    public async Task<OrderListResponse> GetOrdersByStatusAsync(int statusId, int page, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/orders?statusId={statusId}&page={page}&itemsPerPage=50", ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
        return data ?? new OrderListResponse();
    }

    public async Task<ExpeditionOrderDetail> GetExpeditionOrderDetailAsync(string code, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/orders/{code}?include=stockLocation,notes", ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<ExpeditionOrderDetailResponse>(JsonOptions, ct);
        return data!.Data.Order;
    }

    public async Task SetAdditionalFieldAsync(
        string orderCode,
        int index,
        string? text,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(orderCode))
            throw new ArgumentException("Order code must not be null or empty.", nameof(orderCode));
        if (index is < AdditionalFieldMinIndex or > AdditionalFieldMaxIndex)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be between 1 and 6 inclusive.");
        if (text != null && index <= AdditionalFieldShortTextMaxIndex && text.Length > AdditionalFieldShortTextMaxLength)
            throw new ArgumentException($"Text for additionalField index {index} must not exceed 255 characters.", nameof(text));

        var body = new UpdateAdditionalFieldRequest
        {
            Data = new UpdateAdditionalFieldData
            {
                AdditionalFields = [new AdditionalFieldEntry { Index = index, Text = text }],
            },
        };

        var response = await _http.PatchAsJsonAsync($"/api/orders/{orderCode}/notes", body, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"PATCH /api/orders/{orderCode}/notes returned {(int)response.StatusCode}: {errorBody}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<OrderSummary> GetOrderDetailInternalAsync(string code, CancellationToken ct)
    {
        var response = await _http.GetAsync($"/api/orders/{code}", ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions, ct);
        return data!.Data.Order;
    }

    private static EshopOrderSummary MapToSummary(OrderSummary order) =>
        new()
        {
            Code = order.Code,
            ExternalCode = order.ExternalCode,
            Email = order.Email,
            StatusId = order.Status.Id,
        };

    private static EshopOrderInfo MapToOrderInfo(OrderSummary o) => new()
    {
        Code = o.Code,
        CustomerGuid = o.CustomerGuid,
        TotalWithVat = o.Price?.WithVat,
        CurrencyCode = o.Price?.CurrencyCode,
        StatusId = o.Status.Id,
        AdminUrl = o.AdminUrl,
        OrderDate = o.CreationTime is { } t && DateTime.TryParse(t, out var dt) ? dt : null,
    };
}

/// <summary>
/// Internal HTTP body type for POST /api/orders.
/// JSON attributes match the Shoptet REST API contract.
/// </summary>
file class ShoptetCreateOrderBody
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = null!;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = null!;

    [JsonPropertyName("externalCode")]
    public string ExternalCode { get; set; } = null!;

    [JsonPropertyName("shippingGuid")]
    public string ShippingGuid { get; set; } = null!;

    [JsonPropertyName("paymentMethodGuid")]
    public string PaymentMethodGuid { get; set; } = null!;

    [JsonPropertyName("currency")]
    public ShoptetCurrency Currency { get; set; } = new();

    [JsonPropertyName("billingAddress")]
    public ShoptetAddress BillingAddress { get; set; } = new();

    [JsonPropertyName("items")]
    public List<ShoptetOrderItem> Items { get; set; } = new();
}

file class ShoptetCurrency
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "CZK";
}

file class ShoptetAddress
{
    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = null!;

    [JsonPropertyName("street")]
    public string Street { get; set; } = null!;

    [JsonPropertyName("city")]
    public string City { get; set; } = null!;

    [JsonPropertyName("zip")]
    public string Zip { get; set; } = null!;
}

file class ShoptetOrderItem
{
    [JsonPropertyName("itemType")]
    public string ItemType { get; set; } = null!;

    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("vatRate")]
    public string VatRate { get; set; } = null!;

    [JsonPropertyName("itemPriceWithVat")]
    public string ItemPriceWithVat { get; set; } = null!;

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = null!;
}
