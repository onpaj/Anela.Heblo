using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;

/// <summary>
/// Shared address DTO used for both billingAddress and deliveryAddress on invoices.
/// The fields companyId, taxId, vatId, and vatIdValidationStatus are only present
/// on billingAddress — they are absent (null) on deliveryAddress.
/// </summary>
public class ShoptetInvoiceAddressDto
{
    [JsonPropertyName("company")]
    public string? Company { get; set; }

    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("street")]
    public string? Street { get; set; }

    [JsonPropertyName("houseNumber")]
    public string? HouseNumber { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("zip")]
    public string? Zip { get; set; }

    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; set; }

    /// <summary>Only present on billingAddress.</summary>
    [JsonPropertyName("companyId")]
    public string? CompanyId { get; set; }

    /// <summary>Only present on billingAddress.</summary>
    [JsonPropertyName("taxId")]
    public string? TaxId { get; set; }

    /// <summary>Only present on billingAddress.</summary>
    [JsonPropertyName("vatId")]
    public string? VatId { get; set; }

    /// <summary>Only present on billingAddress.</summary>
    [JsonPropertyName("vatIdValidationStatus")]
    public string? VatIdValidationStatus { get; set; }
}
