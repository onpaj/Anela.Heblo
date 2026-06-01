namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;

public class PackingEligibility
{
    public bool IsEligible { get; set; }
    public string? WarningTitle { get; set; }
    public string? WarningBody { get; set; }
}
