namespace Anela.Heblo.Domain.Features.Catalog;

public class CostBreakdown
{
    public decimal M0CostLevel { get; }
    public decimal M1CostLevel { get; }
    public decimal M2CostLevel { get; }
    public decimal M3CostLevel { get; }

    public decimal M0CostTotal => M0CostLevel;
    public decimal M1CostTotal => M0CostLevel + M1CostLevel;
    public decimal M2CostTotal => M0CostLevel + M1CostLevel + M2CostLevel;
    public decimal M3CostTotal => M0CostLevel + M1CostLevel + M2CostLevel + M3CostLevel;

    public CostBreakdown(decimal materialCost, decimal manufacturingCost, decimal salesCost, decimal overheadCost)
    {
        M0CostLevel = Math.Round(materialCost, 2);
        M1CostLevel = Math.Round(manufacturingCost, 2);
        M2CostLevel = Math.Round(salesCost, 2);
        M3CostLevel = Math.Round(overheadCost, 2);
    }

    public static CostBreakdown Zero => new CostBreakdown(0, 0, 0, 0);
}