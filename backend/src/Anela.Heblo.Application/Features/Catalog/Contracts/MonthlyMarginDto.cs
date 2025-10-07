namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class MonthlyMarginDto
{
    public DateTime Month { get; set; }
    public decimal M0Percentage { get; set; }
    public decimal M1Percentage { get; set; }
    public decimal M2Percentage { get; set; }
    public decimal M3Percentage { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal ManufacturingCost { get; set; }
    public decimal SalesCost { get; set; }
    public decimal TotalCosts { get; set; }
}