namespace Anela.Heblo.Application.Features.ShoptetCustomers;

public class ShoptetCustomerInfoDto
{
    public string Guid { get; set; } = null!;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? CustomerGroup { get; set; }
    public string? PriceList { get; set; }
    /// <summary>Pre-formatted from the Shoptet customer's billing address fields (countryCode, city, zip, street).</summary>
    public string? DefaultShippingAddress { get; set; }
}
