namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class MonthlyMarginBreakdownDto
{
    public DateTime Month { get; set; }
    public decimal MarginAmount { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public int UnitsSold { get; set; }
}
