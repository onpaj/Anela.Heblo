namespace Anela.Heblo.Application.Features.ShoptetOrders;

public class EshopOrderSummary
{
    public string Code { get; set; } = null!;
    public string? ExternalCode { get; set; }
    public string? Email { get; set; }
    public int StatusId { get; set; }
}
