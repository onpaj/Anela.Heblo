namespace Anela.Heblo.Application.Features.ShoptetCustomers;

public class ShoptetCustomerInfoDto
{
    public string Guid { get; set; } = null!;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? CustomerGroup { get; set; }
    public string? PriceList { get; set; }
    public string? DefaultShippingAddress { get; set; }
}
