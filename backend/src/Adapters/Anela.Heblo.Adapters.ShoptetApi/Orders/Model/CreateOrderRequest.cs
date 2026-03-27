using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class CreateOrderRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = null!;

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

    [JsonPropertyName("suppressEmailSending")]
    public bool SuppressEmailSending { get; set; } = true;

    [JsonPropertyName("suppressStockMovements")]
    public bool SuppressStockMovements { get; set; } = true;

    [JsonPropertyName("suppressDocumentGeneration")]
    public bool SuppressDocumentGeneration { get; set; } = true;

    [JsonPropertyName("suppressProductChecking")]
    public bool SuppressProductChecking { get; set; } = true;
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
    public string ItemType { get; set; } = "product";

    [JsonPropertyName("code")]
    public string Code { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("vatRate")]
    public decimal VatRate { get; set; }

    [JsonPropertyName("itemPriceWithVat")]
    public decimal ItemPriceWithVat { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}
