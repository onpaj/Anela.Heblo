namespace Anela.Heblo.Domain.Features.Catalog;

public class CostBreakdown
{
    public decimal MaterialCost { get; }
    public decimal ManufacturingCost { get; }
    public decimal SalesCost { get; }
    public decimal OverheadCost { get; }

    public decimal M0Cost => MaterialCost;
    public decimal M1Cost => MaterialCost + ManufacturingCost;
    public decimal M2Cost => MaterialCost + ManufacturingCost + SalesCost;
    public decimal M3Cost => MaterialCost + ManufacturingCost + SalesCost + OverheadCost;

    public CostBreakdown(decimal materialCost, decimal manufacturingCost, decimal salesCost, decimal overheadCost)
    {
        MaterialCost = Math.Round(materialCost, 2);
        ManufacturingCost = Math.Round(manufacturingCost, 2);
        SalesCost = Math.Round(salesCost, 2);
        OverheadCost = Math.Round(overheadCost, 2);
    }

    public static CostBreakdown Zero => new CostBreakdown(0, 0, 0, 0);
}