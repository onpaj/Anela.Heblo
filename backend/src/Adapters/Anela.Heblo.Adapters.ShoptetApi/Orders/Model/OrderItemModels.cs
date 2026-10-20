using System.Text.Json.Serialization;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

/// <summary>GET /api/orders/{code} — just the status, billing method and items[] (shoptet-api.md §3.9, §3.17).</summary>
public class OrderItemsDetailResponse
{
    [JsonPropertyName("data")]
    public OrderItemsDetailData? Data { get; set; }
}

public class OrderItemsDetailData
{
    [JsonPropertyName("order")]
    public OrderItemsDetail? Order { get; set; }
}

public class OrderItemsDetail
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public OrderItemsStatus Status { get; set; } = new();

    [JsonPropertyName("items")]
    public List<ShoptetOrderLine> Items { get; set; } = new();

    /// <summary>Null on orders keyed in the admin without a payment method.</summary>
    [JsonPropertyName("billingMethod")]
    public ShoptetBillingMethodDto? BillingMethod { get; set; }
}

public class OrderItemsStatus
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}

/// <summary>One items[] entry of an order. Numbers arrive as strings (shoptet-api.md §3.17).</summary>
public class ShoptetOrderLine
{
    [JsonPropertyName("itemId")]
    public long ItemId { get; set; }

    [JsonPropertyName("itemType")]
    public string ItemType { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("priceRatio")]
    public string? PriceRatio { get; set; }

    [JsonPropertyName("remark")]
    public string? Remark { get; set; }

    [JsonPropertyName("unitPrice")]
    public ShoptetItemUnitPrice? UnitPrice { get; set; }
}

public class ShoptetItemUnitPrice
{
    [JsonPropertyName("withVat")]
    public string? WithVat { get; set; }

    [JsonPropertyName("vatRate")]
    public string? VatRate { get; set; }
}

/// <summary>POST /api/orders/{code}/item body. All numbers are strings (shoptet-api.md §3.17).</summary>
public class AddOrderItemRequest
{
    [JsonPropertyName("data")]
    public AddOrderItemData Data { get; set; } = new();
}

public class AddOrderItemData
{
    [JsonPropertyName("itemType")]
    public string ItemType { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    [JsonPropertyName("unitPriceWithVat")]
    public string UnitPriceWithVat { get; set; } = string.Empty;

    [JsonPropertyName("vatRate")]
    public string VatRate { get; set; } = string.Empty;

    [JsonPropertyName("priceRatio")]
    public string PriceRatio { get; set; } = string.Empty;

    [JsonPropertyName("remark")]
    public string? Remark { get; set; }
}

/// <summary>Shoptet error envelope: {"data":null,"errors":[{"errorCode","message","instance"}]}.</summary>
public class ShoptetErrorResponse
{
    [JsonPropertyName("errors")]
    public List<ShoptetError>? Errors { get; set; }
}

public class ShoptetError
{
    [JsonPropertyName("errorCode")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
