using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;

public class ShoptetInvoiceItemDto
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Quantity as a string, e.g. "2.00"</summary>
    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("amountUnit")]
    public string? AmountUnit { get; set; }

    /// <summary>Item type, e.g. "product", "discount-coupon", "shipping".</summary>
    [JsonPropertyName("itemType")]
    public string? ItemType { get; set; }

    /// <summary>Discount ratio applied to unit price (0.0 = free, 1.0 = no discount). Quoted string per Shoptet API convention.</summary>
    [JsonPropertyName("priceRatio")]
    public string? PriceRatio { get; set; }

    /// <summary>Total price for the line item (amount × unitPrice).</summary>
    [JsonPropertyName("itemPrice")]
    public ShoptetInvoiceUnitPriceDto? ItemPrice { get; set; }

    /// <summary>Unit price for a single piece.</summary>
    [JsonPropertyName("unitPrice")]
    public ShoptetInvoiceUnitPriceDto? UnitPrice { get; set; }
}
