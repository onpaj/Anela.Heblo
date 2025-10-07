namespace Anela.Heblo.Domain.Features.Catalog;

public class MonthlyMarginData : MarginData
{
    public DateTime Month { get; init; }
    public CostBreakdown CostsForMonth { get; init; } = CostBreakdown.Zero;
}