using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class OrderListResponse
{
    [JsonPropertyName("data")]
    public OrderListData Data { get; set; } = new();
}

public class OrderListData
{
    [JsonPropertyName("orders")]
    public List<OrderSummary> Orders { get; set; } = new();

    [JsonPropertyName("paginator")]
    public Paginator Paginator { get; set; } = new();
}

/// <summary>
/// Order summary as returned by GET /api/orders (list endpoint).
/// Available fields: code, guid, creationTime, changeTime, company, fullName, email,
/// phone, remark, cashDeskOrder, customerGuid, paid, status, source, price,
/// paymentMethod, shipping, adminUrl, salesChannelGuid.
/// Note: externalCode and billing/delivery addresses are NOT in the list response —
/// use GET /api/orders/{code} to retrieve them.
/// </summary>
public class OrderSummary
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = null!;

    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("externalCode")]
    public string? ExternalCode { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("company")]
    public string? Company { get; set; }

    /// <summary>ISO 8601 datetime string, e.g. "2024-06-01T10:00:00"</summary>
    [JsonPropertyName("creationTime")]
    public string? CreationTime { get; set; }

    /// <summary>ISO 8601 datetime string</summary>
    [JsonPropertyName("changeTime")]
    public string? ChangeTime { get; set; }

    [JsonPropertyName("paid")]
    public bool? Paid { get; set; }

    [JsonPropertyName("status")]
    public OrderStatusSummary Status { get; set; } = new();

    [JsonPropertyName("shipping")]
    public OrderShippingSummary? Shipping { get; set; }

    [JsonPropertyName("paymentMethod")]
    public OrderPaymentMethodSummary? PaymentMethod { get; set; }
}

public class OrderStatusSummary
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}

/// <summary>
/// Shipping method as returned in the order list response.
/// Shoptet REST API returns { "guid": "...", "name": "..." } — NOT a numeric id.
/// The numeric shippingId values (e.g. 21 for Zásilkovna) are admin-UI internal
/// identifiers used only for Playwright URL filters (?f[shippingId]=21).
/// </summary>
public class OrderShippingSummary
{
    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class OrderPaymentMethodSummary
{
    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class Paginator
{
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }
}
