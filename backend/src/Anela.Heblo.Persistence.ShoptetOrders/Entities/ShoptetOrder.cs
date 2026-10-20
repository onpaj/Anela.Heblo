namespace Anela.Heblo.Persistence.ShoptetOrders.Entities;

/// <summary>
/// One row per Shoptet order header, mirrored from GET /api/orders/{code}.
/// Keyed on the Shoptet order code, which is stable for the life of the order.
/// </summary>
public class ShoptetOrder
{
    public string Code { get; set; } = "";
    public string? Guid { get; set; }
    public string? ExternalCode { get; set; }

    public DateTimeOffset CreationTime { get; set; }
    public DateTimeOffset? ChangeTime { get; set; }

    /// <summary>
    /// CreationTime projected into the store's timezone (Europe/Prague) and truncated to a day.
    /// Every month-grain read view groups on this, so the timezone decision is made once, here.
    /// </summary>
    public DateOnly OrderDate { get; set; }

    public int StatusId { get; set; }
    public string? StatusName { get; set; }
    public bool IsPaid { get; set; }

    public string? CustomerGuid { get; set; }
    /// <summary>Lower-cased, trimmed e-mail. Null for cash-desk orders, which carry no identity at all.</summary>
    public string? CustomerEmail { get; set; }
    public string? BillingCompany { get; set; }
    public string? BillingCompanyId { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingZip { get; set; }
    public string? BillingCountryCode { get; set; }

    public bool CashDeskOrder { get; set; }
    public string? SalesChannelGuid { get; set; }
    public int? SourceId { get; set; }
    public string? SourceName { get; set; }

    public string? ShippingGuid { get; set; }
    public string? ShippingName { get; set; }
    public string? PaymentMethodGuid { get; set; }
    public string? PaymentMethodName { get; set; }
    public int? BillingMethodId { get; set; }
    public string? BillingMethodName { get; set; }

    public string? CurrencyCode { get; set; }
    public decimal? ExchangeRate { get; set; }
    public decimal? PriceWithVat { get; set; }
    public decimal? PriceWithoutVat { get; set; }
    public decimal? PriceVat { get; set; }
    public decimal? PriceToPay { get; set; }

    /// <summary>Sum of the itemType=shipping lines — what the customer was charged for delivery.</summary>
    public decimal ShippingPriceWithVat { get; set; }
    public decimal ShippingPriceWithoutVat { get; set; }
    /// <summary>Sum of the itemType=billing lines — the payment-method surcharge (e.g. cash on delivery).</summary>
    public decimal BillingPriceWithVat { get; set; }
    public decimal BillingPriceWithoutVat { get; set; }
    /// <summary>Sum of the discount-coupon and volume-discount lines (negative as Shoptet returns them).</summary>
    public decimal DiscountWithVat { get; set; }
    public decimal DiscountWithoutVat { get; set; }
    /// <summary>Sum of the itemType=product and itemType=product-set lines — merchandise revenue only.</summary>
    public decimal ProductPriceWithVat { get; set; }
    public decimal ProductPriceWithoutVat { get; set; }
    /// <summary>Number of merchandise units on the order (a set counts as its own quantity, not its components).</summary>
    public decimal ProductUnits { get; set; }

    public bool VatPayer { get; set; }
    public string? VatMode { get; set; }
    public string? Language { get; set; }
    public int? StockId { get; set; }
    public string? Referer { get; set; }

    public string RawPayload { get; set; } = "{}";
    public DateTimeOffset SyncedAt { get; set; }

    public List<ShoptetOrderItem> Items { get; set; } = new();
}
