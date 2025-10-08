namespace Anela.Heblo.Application.Features.FinancialOverview;

public class FinancialAnalysisOptions
{
    public const string ConfigKey = "FinancialAnalysisOptions";

    public int MonthsToCache { get; set; } = 24;
}