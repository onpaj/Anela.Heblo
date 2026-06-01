namespace Anela.Heblo.Application.Features.ShoptetOrders;

public class EshopOrderInfo
{
    public string Code { get; set; } = null!;
    public string? CustomerGuid { get; set; }
    public decimal? TotalWithVat { get; set; }
    public string? CurrencyCode { get; set; }
    public int StatusId { get; set; }
    public string? AdminUrl { get; set; }
    public DateTime? OrderDate { get; set; }
}
