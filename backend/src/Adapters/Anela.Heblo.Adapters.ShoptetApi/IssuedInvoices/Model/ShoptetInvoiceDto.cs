using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;

public class ShoptetInvoiceDto
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = null!;

    /// <summary>Order code this invoice belongs to.</summary>
    [JsonPropertyName("orderCode")]
    public string? OrderCode { get; set; }

    /// <summary>Variable symbol (payment reference). Returned as a number by the API.</summary>
    [JsonPropertyName("varSymbol")]
    public long? VarSymbol { get; set; }

    /// <summary>ISO 8601 datetime string, e.g. "2024-06-01T10:00:00"</summary>
    [JsonPropertyName("creationTime")]
    public string? CreationTime { get; set; }

    /// <summary>ISO 8601 date string, e.g. "2024-06-15T00:00:00"</summary>
    [JsonPropertyName("dueDate")]
    public string? DueDate { get; set; }

    /// <summary>ISO 8601 date string for tax purposes.</summary>
    [JsonPropertyName("taxDate")]
    public string? TaxDate { get; set; }

    [JsonPropertyName("shipping")]
    public ShoptetInvoiceShippingDto? Shipping { get; set; }

    [JsonPropertyName("billingMethod")]
    public ShoptetBillingMethodDto? BillingMethod { get; set; }

    [JsonPropertyName("billingAddress")]
    public ShoptetInvoiceAddressDto? BillingAddress { get; set; }

    [JsonPropertyName("deliveryAddress")]
    public ShoptetInvoiceAddressDto? DeliveryAddress { get; set; }

    [JsonPropertyName("items")]
    public List<ShoptetInvoiceItemDto> Items { get; set; } = new();

    [JsonPropertyName("price")]
    public ShoptetInvoicePriceDto? Price { get; set; }
}
