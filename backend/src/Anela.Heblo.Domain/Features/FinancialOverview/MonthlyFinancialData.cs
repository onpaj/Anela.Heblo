namespace Anela.Heblo.Domain.Features.FinancialOverview;

public class MonthlyFinancialData
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal FinancialBalance => Income - Expenses;

    public string MonthYearDisplay => $"{Month:D2}/{Year}";

    public DateTime GetMonthStart()
    {
        return new DateTime(Year, Month, 1);
    }

    public DateTime GetMonthEnd()
    {
        return GetMonthStart().AddMonths(1).AddDays(-1);
    }
}