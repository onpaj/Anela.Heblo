using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Customers.Model;

// TODO: Verify field names against live Shoptet API before production.
// Run: curl -H "Shoptet-Private-API-Token: <token>" https://api.myshoptet.com/api/customers/<guid>
// Update docs/integrations/shoptet-api.md with actual response shape.
public class ShoptetCustomerResponse
{
    [JsonPropertyName("data")]
    public ShoptetCustomerData? Data { get; set; }
}

public class ShoptetCustomerData
{
    [JsonPropertyName("customer")]
    public ShoptetCustomerDetail? Customer { get; set; }
}

public class ShoptetCustomerDetail
{
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = null!;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("customerGroup")]
    public ShoptetNamedItem? CustomerGroup { get; set; }

    [JsonPropertyName("priceList")]
    public ShoptetNamedItem? PriceList { get; set; }

    [JsonPropertyName("billingAddress")]
    public ShoptetCustomerAddress? BillingAddress { get; set; }
}

public class ShoptetNamedItem
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class ShoptetCustomerAddress
{
    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("street")]
    public string? Street { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("zip")]
    public string? Zip { get; set; }

    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; set; }
}
