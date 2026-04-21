using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;

public class ShoptetInvoiceUnitPriceDto
{
    [JsonPropertyName("withVat")]
    public string? WithVat { get; set; }

    [JsonPropertyName("withoutVat")]
    public string? WithoutVat { get; set; }

    [JsonPropertyName("vat")]
    public string? Vat { get; set; }

    /// <summary>VAT rate as a quoted string, e.g. "21.0" — same pattern as all other Shoptet price fields.</summary>
    [JsonPropertyName("vatRate")]
    public string? VatRate { get; set; }
}
