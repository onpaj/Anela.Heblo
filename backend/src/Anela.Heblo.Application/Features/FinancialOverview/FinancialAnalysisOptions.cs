namespace Anela.Heblo.Application.Features.FinancialOverview;

public class FinancialAnalysisOptions
{
    public const string ConfigKey = "FinancialAnalysisOptions";

    public int MonthsToCache { get; set; } = 24;

    /// <summary>
    /// Days subtracted from today to pick the comparison cutoff date, absorbing ERP data lag.
    /// The partial (cutoff) month is cut at this date's day-of-month across every compared year.
    /// </summary>
    public int PartialMonthLagDays { get; set; } = 5;
}