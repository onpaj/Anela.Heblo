namespace Anela.Heblo.Application.Features.Smartsupp.Contracts;

public class ShoptetContactInfoDto
{
    public ShoptetCustomerSnapshotDto? Customer { get; init; }
    public List<ShoptetOrderSnapshotDto> RecentOrders { get; init; } = new();
    public DateTime? CartUpdatedAt { get; init; }
}

public class ShoptetCustomerSnapshotDto
{
    public string? FullName { get; init; }
    public string? Email { get; init; }
    public string? CustomerGroup { get; init; }
    public string? PriceList { get; init; }
    public string? DefaultShippingAddress { get; init; }
}

public class ShoptetOrderSnapshotDto
{
    public required string Code { get; init; }
    public string? StatusName { get; init; }
    public decimal? TotalWithVat { get; init; }
    public string? CurrencyCode { get; init; }
    public DateTime? OrderDate { get; init; }
    public string? AdminUrl { get; init; }
}
