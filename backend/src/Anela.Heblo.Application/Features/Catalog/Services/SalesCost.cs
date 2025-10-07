namespace Anela.Heblo.Application.Features.Catalog.Services;

public class SalesCost
{
    public DateTime Date { get; set; }
    public decimal MarketingCost { get; set; }
    public decimal StorageCost { get; set; }
    public decimal Total => MarketingCost + StorageCost;
}