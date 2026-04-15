using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;

/// <summary>
/// Shipping info as returned in the invoice response.
/// Shoptet REST API returns a GUID that maps to a shipping method.
/// </summary>
public class ShoptetInvoiceShippingDto
{
    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
