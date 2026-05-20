namespace Anela.Heblo.Application.Features.Smartsupp.Contracts;

public class ShoptetContactInfoDto
{
    public ShoptetCustomerSnapshotDto Customer { get; set; } = null!;
    public List<ShoptetOrderSnapshotDto> RecentOrders { get; set; } = new();
    public DateTime? CartUpdatedAt { get; set; }
}

public class ShoptetCustomerSnapshotDto
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? CustomerGroup { get; set; }
    public string? PriceList { get; set; }
    public string? DefaultShippingAddress { get; set; }
}

public class ShoptetOrderSnapshotDto
{
    public string Code { get; set; } = null!;
    public string? StatusName { get; set; }
    public decimal? TotalWithVat { get; set; }
    public string? CurrencyCode { get; set; }
    public DateTime? OrderDate { get; set; }
    public string? AdminUrl { get; set; }
}
