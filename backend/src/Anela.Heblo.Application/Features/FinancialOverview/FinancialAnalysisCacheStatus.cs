namespace Anela.Heblo.Application.Features.FinancialOverview;

public class FinancialAnalysisCacheStatus
{
    public DateTime LastRefresh { get; set; }
    public int CachedMonthsCount { get; set; }
    public int CachedStockMonthsCount { get; set; }
}