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

    [JsonPropertyName("itemType")]
    public string? ItemType { get; set; }

    [JsonPropertyName("priceRatio")]
    public decimal? PriceRatio { get; set; }

    /// <summary>Total price for the line item (amount × unitPrice).</summary>
    [JsonPropertyName("itemPrice")]
    public ShoptetInvoiceUnitPriceDto? ItemPrice { get; set; }

    /// <summary>Unit price for a single piece.</summary>
    [JsonPropertyName("unitPrice")]
    public ShoptetInvoiceUnitPriceDto? UnitPrice { get; set; }
}
