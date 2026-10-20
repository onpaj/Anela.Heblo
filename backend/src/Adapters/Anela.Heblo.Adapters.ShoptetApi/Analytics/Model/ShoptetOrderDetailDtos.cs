using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics.Model;

/// <summary>
/// Read-only mirror of GET /api/orders/{code}. Shoptet returns monetary values as JSON strings
/// and quantities as JSON numbers, so every decimal allows reading from a string.
/// Shape verified live against the anela.cz store on 2026-09-22 — see docs/integrations/shoptet-api.md §3.9.
/// </summary>
public class ShoptetOrderDetailResponse
{
    [JsonPropertyName("data")]
    public ShoptetOrderDetailData? Data { get; set; }
}

public class ShoptetOrderDetailData
{
    [JsonPropertyName("order")]
    public ShoptetOrderDetailDto? Order { get; set; }
}

public class ShoptetOrderDetailDto
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("guid")] public string? Guid { get; set; }
    [JsonPropertyName("externalCode")] public string? ExternalCode { get; set; }
    [JsonPropertyName("customerGuid")] public string? CustomerGuid { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("phone")] public string? Phone { get; set; }
    [JsonPropertyName("creationTime")][JsonConverter(typeof(ShoptetDateTimeOffsetConverter))] public DateTimeOffset? CreationTime { get; set; }
    [JsonPropertyName("changeTime")][JsonConverter(typeof(ShoptetDateTimeOffsetConverter))] public DateTimeOffset? ChangeTime { get; set; }
    [JsonPropertyName("cashDeskOrder")] public bool? CashDeskOrder { get; set; }
    [JsonPropertyName("salesChannelGuid")] public string? SalesChannelGuid { get; set; }
    [JsonPropertyName("stockId")] public int? StockId { get; set; }
    [JsonPropertyName("vatPayer")] public bool? VatPayer { get; set; }
    [JsonPropertyName("vatMode")] public string? VatMode { get; set; }
    // Nullable: the live store returns "paid": null on some historical orders
    // (observed 2026-09-22 on September 2026 orders), and the same holds for the other flags.
    [JsonPropertyName("paid")] public bool? Paid { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("referer")] public string? Referer { get; set; }
    [JsonPropertyName("billingAddress")] public ShoptetOrderAddressDto? BillingAddress { get; set; }
    [JsonPropertyName("price")] public ShoptetOrderPriceDto? Price { get; set; }
    [JsonPropertyName("source")] public ShoptetOrderSourceDto? Source { get; set; }
    [JsonPropertyName("status")] public ShoptetOrderStatusDto? Status { get; set; }
    [JsonPropertyName("billingMethod")] public ShoptetOrderBillingMethodDto? BillingMethod { get; set; }
    [JsonPropertyName("paymentMethod")] public ShoptetOrderNamedGuidDto? PaymentMethod { get; set; }
    [JsonPropertyName("shipping")] public ShoptetOrderNamedGuidDto? Shipping { get; set; }
    [JsonPropertyName("items")] public List<ShoptetOrderItemDto> Items { get; set; } = new();

    /// <summary>
    /// Mirrors items[] for ordinary products and additionally carries the product-set components
    /// as itemType "product-set-item". Only those component entries are persisted from here —
    /// the rest are duplicates of items[]. See docs/integrations/shoptet-api.md §3.3.
    /// </summary>
    [JsonPropertyName("completion")] public List<ShoptetOrderItemDto> Completion { get; set; } = new();
}

public class ShoptetOrderAddressDto
{
    [JsonPropertyName("company")] public string? Company { get; set; }
    [JsonPropertyName("companyId")] public string? CompanyId { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("zip")] public string? Zip { get; set; }
    [JsonPropertyName("countryCode")] public string? CountryCode { get; set; }
}

public class ShoptetOrderPriceDto
{
    [JsonPropertyName("currencyCode")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("withVat")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? WithVat { get; set; }
    [JsonPropertyName("withoutVat")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? WithoutVat { get; set; }
    [JsonPropertyName("vat")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Vat { get; set; }
    [JsonPropertyName("toPay")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ToPay { get; set; }
    [JsonPropertyName("exchangeRate")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ExchangeRate { get; set; }
    [JsonPropertyName("vatRate")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? VatRate { get; set; }
}

public class ShoptetOrderSourceDto
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public class ShoptetOrderStatusDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public class ShoptetOrderBillingMethodDto
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public class ShoptetOrderNamedGuidDto
{
    [JsonPropertyName("guid")] public string? Guid { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public class ShoptetOrderItemDto
{
    [JsonPropertyName("itemId")] public long? ItemId { get; set; }
    [JsonPropertyName("parentProductSetItemId")] public long? ParentProductSetItemId { get; set; }
    [JsonPropertyName("itemType")] public string? ItemType { get; set; }
    [JsonPropertyName("productType")] public string? ProductType { get; set; }
    [JsonPropertyName("productGuid")] public string? ProductGuid { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("variantName")] public string? VariantName { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("ean")] public string? Ean { get; set; }
    [JsonPropertyName("amountUnit")] public string? AmountUnit { get; set; }

    [JsonPropertyName("amount")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Amount { get; set; }

    [JsonPropertyName("weight")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Weight { get; set; }

    /// <summary>Line total — unit price × amount. Absent on completion[] entries.</summary>
    [JsonPropertyName("itemPrice")] public ShoptetOrderPriceDto? ItemPrice { get; set; }
    [JsonPropertyName("unitPrice")] public ShoptetOrderPriceDto? UnitPrice { get; set; }
    [JsonPropertyName("purchasePrice")] public ShoptetOrderPriceDto? PurchasePrice { get; set; }
}
