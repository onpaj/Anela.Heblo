using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Marketing;

public class MarketingPerformanceRepository : IMarketingPerformanceRepository
{
    private readonly ApplicationDbContext _context;

    public MarketingPerformanceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<MarketingPerformanceMonth>> GetRangeAsync(YearMonth from, YearMonth to, CancellationToken cancellationToken)
    {
        var fromKey = from.Year * 100 + from.Month;
        var toKey = to.Year * 100 + to.Month;
        return await _context.MarketingPerformanceMonths
            .AsNoTracking()
            .Include(m => m.ChannelCosts)
            .Where(m => m.Year * 100 + m.Month >= fromKey && m.Year * 100 + m.Month <= toKey)
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .ToListAsync(cancellationToken);
    }

    public Task<MarketingPerformanceMonth?> GetForUpdateAsync(YearMonth month, CancellationToken cancellationToken) =>
        _context.MarketingPerformanceMonths
            .Include(m => m.ChannelCosts)
            .SingleOrDefaultAsync(m => m.Year == month.Year && m.Month == month.Month, cancellationToken);

    public async Task AddAsync(MarketingPerformanceMonth month, CancellationToken cancellationToken) =>
        await _context.MarketingPerformanceMonths.AddAsync(month, cancellationToken);

    public async Task<int> LockMonthsBeforeAsync(YearMonth cutoff, CancellationToken cancellationToken)
    {
        // Load + set rather than ExecuteUpdate: the InMemory provider used in tests does not support ExecuteUpdate.
        var cutoffKey = cutoff.Year * 100 + cutoff.Month;
        var rows = await _context.MarketingPerformanceMonths
            .Where(m => !m.IsLocked && m.Year * 100 + m.Month < cutoffKey)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.IsLocked = true;
        }
        await _context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<DateTime?> GetLastComputedAtAsync(CancellationToken cancellationToken)
    {
        var revenue = await _context.MarketingPerformanceMonths.MaxAsync(m => m.RevenueComputedAt, cancellationToken);
        var costs = await _context.MarketingPerformanceMonths.MaxAsync(m => m.CostsComputedAt, cancellationToken);
        if (revenue is null) return costs;
        if (costs is null) return revenue;
        return revenue > costs ? revenue : costs;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _context.SaveChangesAsync(cancellationToken);

    public void Detach(MarketingPerformanceMonth month)
    {
        // Detaching only the parent leaves its ChannelCosts children (still Added/Modified from this failed
        // save) tracked, ready to poison the next unrelated SaveChangesAsync call. Detach them explicitly too.
        foreach (var cost in month.ChannelCosts.ToList())
        {
            _context.Entry(cost).State = EntityState.Detached;
        }
        _context.Entry(month).State = EntityState.Detached;
    }
}
