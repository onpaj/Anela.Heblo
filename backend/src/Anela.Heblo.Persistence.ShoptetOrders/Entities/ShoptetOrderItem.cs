namespace Anela.Heblo.Persistence.ShoptetOrders.Entities;

/// <summary>
/// One row per order line. Carries both levels of the Shoptet product-set contract:
/// the set header from <c>items[]</c> (ItemType = product-set, priced) and its components
/// from <c>completion[]</c> (ItemType = product-set-item, unpriced), linked by ParentItemId.
/// See docs/integrations/shoptet-api.md §3.3.
/// </summary>
public class ShoptetOrderItem
{
    public string OrderCode { get; set; } = "";

    /// <summary>
    /// Position within the persisted line sequence: <c>items[]</c> first, then the
    /// <c>product-set-item</c> entries of <c>completion[]</c>. Assigned at ingest so the
    /// primary key stays stable when an order is re-synced (lines are replaced wholesale).
    /// </summary>
    public int LineNo { get; set; }

    /// <summary>Which API array the line came from: "items" or "completion".</summary>
    public string SourceArray { get; set; } = "";

    /// <summary>
    /// Shoptet itemId. Unique per order for <c>items[]</c> lines; for <c>product-set-item</c>
    /// lines it is the catalogue item id of the component and is NOT unique within the order.
    /// </summary>
    public long? ItemId { get; set; }

    /// <summary>parentProductSetItemId — the ItemId of the owning product-set line.</summary>
    public long? ParentItemId { get; set; }

    public string ItemType { get; set; } = "";
    public string? ProductType { get; set; }
    public string? ProductGuid { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public string? VariantName { get; set; }
    public string? Brand { get; set; }
    public string? Ean { get; set; }

    /// <summary>
    /// Quantity. For product-set-item lines this is ALREADY the order total
    /// (component-per-set × set count) — never multiply it by the set quantity.
    /// </summary>
    public decimal? Amount { get; set; }
    public string? AmountUnit { get; set; }
    public decimal? Weight { get; set; }

    public decimal? UnitPriceWithVat { get; set; }
    public decimal? UnitPriceWithoutVat { get; set; }
    public decimal? VatRate { get; set; }

    /// <summary>Line total (Shoptet <c>itemPrice</c>) — unit price × amount, not the unit price.</summary>
    public decimal? LinePriceWithVat { get; set; }
    public decimal? LinePriceWithoutVat { get; set; }
    public decimal? LinePriceVat { get; set; }

    public decimal? PurchasePriceWithoutVat { get; set; }

    public DateTimeOffset SyncedAt { get; set; }

    public ShoptetOrder? Order { get; set; }
}
