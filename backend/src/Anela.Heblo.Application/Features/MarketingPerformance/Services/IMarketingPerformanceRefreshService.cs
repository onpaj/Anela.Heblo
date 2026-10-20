using Anela.Heblo.Domain.Features.MarketingPerformance;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

public class MonthRefreshOutcome
{
    public YearMonth Month { get; init; }
    public bool RevenueOk { get; init; }
    public bool CostsOk { get; init; }
    public string? Error { get; init; }
    public int UnmatchedVatIdCount { get; init; }
}

public class RefreshRunResult
{
    public List<MonthRefreshOutcome> Months { get; init; } = new();
    public int LockedMonths { get; init; }
    public bool AllFailed => Months.Count > 0 && Months.All(m => !m.RevenueOk && !m.CostsOk);
}

public interface IMarketingPerformanceRefreshService
{
    Task<RefreshRunResult> RefreshWindowAsync(CancellationToken cancellationToken);
    Task<RefreshRunResult> RecomputeRangeAsync(YearMonth from, YearMonth to, CancellationToken cancellationToken);
}
