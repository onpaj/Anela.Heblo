using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;

/// <summary>
/// Billing/payment method as returned in the invoice response.
/// Shoptet REST API returns { "id": int, "name": "..." }.
/// </summary>
public class ShoptetBillingMethodDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
