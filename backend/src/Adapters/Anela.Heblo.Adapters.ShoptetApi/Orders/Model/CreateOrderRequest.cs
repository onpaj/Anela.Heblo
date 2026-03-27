using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

// The Shoptet REST API wraps the order body in {"data": {...}}.
// ShoptetOrderClient.CreateOrderAsync wraps this in the envelope before posting.
public class CreateOrderRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = null!;

    // Required. Must be in international format e.g. "+420725191660".
    [JsonPropertyName("phone")]
    public string Phone { get; set; } = null!;

    [JsonPropertyName("externalCode")]
    public string ExternalCode { get; set; } = null!;

    [JsonPropertyName("shippingGuid")]
    public string ShippingGuid { get; set; } = null!;

    [JsonPropertyName("paymentMethodGuid")]
    public string PaymentMethodGuid { get; set; } = null!;

    [JsonPropertyName("currency")]
    public OrderCurrency Currency { get; set; } = new();

    [JsonPropertyName("billingAddress")]
    public OrderAddress BillingAddress { get; set; } = new();

    [JsonPropertyName("items")]
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderCurrency
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "CZK";
}

public class OrderAddress
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

public class OrderItem
{
    [JsonPropertyName("itemType")]
    public string ItemType { get; set; } = null!;

    // Required for product/service types. Omit for billing/shipping items.
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    // Shoptet requires numeric fields as strings (not JSON numbers).
    [JsonPropertyName("vatRate")]
    public string VatRate { get; set; } = null!;

    [JsonPropertyName("itemPriceWithVat")]
    public string ItemPriceWithVat { get; set; } = null!;

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = null!;
}
