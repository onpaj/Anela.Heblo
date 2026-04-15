using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;

public class ShoptetInvoicePriceDto
{
    [JsonPropertyName("withVat")]
    public string? WithVat { get; set; }

    [JsonPropertyName("withoutVat")]
    public string? WithoutVat { get; set; }

    [JsonPropertyName("toPay")]
    public string? ToPay { get; set; }

    [JsonPropertyName("vat")]
    public string? Vat { get; set; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("exchangeRate")]
    public decimal? ExchangeRate { get; set; }
}
