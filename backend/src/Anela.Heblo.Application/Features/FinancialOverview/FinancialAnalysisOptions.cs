namespace Anela.Heblo.Application.Features.FinancialOverview;

public class FinancialAnalysisOptions
{
    public const string ConfigKey = "FinancialAnalysisOptions";

    /// <summary>
    /// How often to refresh the cached financial data (default: 3 hours)
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(3);

    /// <summary>
    /// How many months of data to cache (default: 24 months = 2 years)
    /// </summary>
    public int MonthsToCache { get; set; } = 24;
}