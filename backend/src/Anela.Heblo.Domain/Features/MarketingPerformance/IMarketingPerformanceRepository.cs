namespace Anela.Heblo.Domain.Features.MarketingPerformance;

public interface IMarketingPerformanceRepository
{
    /// <summary>Months in [from, to] inclusive with ChannelCosts loaded, ascending. Missing months are absent.</summary>
    Task<List<MarketingPerformanceMonth>> GetRangeAsync(YearMonth from, YearMonth to, CancellationToken cancellationToken);

    /// <summary>Tracked row for one month with ChannelCosts, or null.</summary>
    Task<MarketingPerformanceMonth?> GetForUpdateAsync(YearMonth month, CancellationToken cancellationToken);

    Task AddAsync(MarketingPerformanceMonth month, CancellationToken cancellationToken);

    /// <summary>Marks every unlocked month strictly before <paramref name="cutoff"/> as locked. Returns rows affected.</summary>
    Task<int> LockMonthsBeforeAsync(YearMonth cutoff, CancellationToken cancellationToken);

    /// <summary>Latest RevenueComputedAt/CostsComputedAt across all months, for the screen's status line.</summary>
    Task<DateTime?> GetLastComputedAtAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Detaches a tracked month (and its ChannelCosts children) from the change tracker. Call after a
    /// failed SaveChangesAsync so that row's still-pending changes don't resurface at a later, unrelated
    /// SaveChangesAsync for a different month.</summary>
    void Detach(MarketingPerformanceMonth month);
}
