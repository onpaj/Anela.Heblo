namespace Anela.Heblo.Application.Features.ShoptetOrders;

public class EshopOrderSummary
{
    public string Code { get; set; } = null!;
    public string? ExternalCode { get; set; }
    public string? Email { get; set; }
    public int StatusId { get; set; }
    public DateTimeOffset? CreationTime { get; set; }

    /// <summary>Order header total incl. VAT in the order currency. Null when Shoptet sends no price.</summary>
    public decimal? PriceWithVat { get; set; }
}
