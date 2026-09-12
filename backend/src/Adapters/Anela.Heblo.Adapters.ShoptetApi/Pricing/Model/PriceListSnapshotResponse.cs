using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Pricing.Model;

public class PriceListSnapshotResponse
{
    [JsonPropertyName("data")]
    public PriceListSnapshotData? Data { get; set; }
}

public class PriceListSnapshotData
{
    [JsonPropertyName("pricelist")]
    public List<PriceListSnapshotItem> Items { get; set; } = new();

    [JsonPropertyName("paginator")]
    public PriceListPaginator? Paginator { get; set; }
}

public class PriceListSnapshotItem
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Whether <see cref="PriceListItemPrice.Price"/> already includes VAT. A real per-item
    /// flag — never assume its value, always read it. On the Anela store it is <c>true</c>
    /// for the retail list.
    /// </summary>
    [JsonPropertyName("includingVat")]
    public bool IncludingVat { get; set; }

    /// <summary>VAT percentage as a string, e.g. "21.00".</summary>
    [JsonPropertyName("vatRate")]
    public string? VatRate { get; set; }

    [JsonPropertyName("price")]
    public PriceListItemPrice? Price { get; set; }
}

public class PriceListItemPrice
{
    /// <summary>
    /// The stored price as a string with 2 decimals (e.g. "390.00"), or <c>null</c> when the
    /// product has no price set in this list. Its VAT meaning depends on the sibling
    /// <see cref="PriceListSnapshotItem.IncludingVat"/> flag, not on this field alone.
    /// </summary>
    [JsonPropertyName("price")]
    public string? Price { get; set; }

    /// <summary>Writable only on the default price list; exposed for completeness, not used by the sync.</summary>
    [JsonPropertyName("buyPrice")]
    public string? BuyPrice { get; set; }
}

public class PriceListPaginator
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }
}
