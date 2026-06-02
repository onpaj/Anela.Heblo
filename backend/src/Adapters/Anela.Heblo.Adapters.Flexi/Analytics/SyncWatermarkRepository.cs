using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public sealed class SyncWatermarkRepository : ISyncWatermarkRepository
{
    private readonly AnalyticsDbContext _dbContext;

    public SyncWatermarkRepository(AnalyticsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SyncState> GetOrCreateAsync(string entityName, CancellationToken ct = default)
    {
        var state = await _dbContext.SyncStates.FindAsync([entityName], ct);
        if (state != null)
            return state;

        state = new SyncState { EntityName = entityName };
        _dbContext.SyncStates.Add(state);
        await _dbContext.SaveChangesAsync(ct);
        return state;
    }

    public async Task SaveAsync(SyncState state, CancellationToken ct = default)
    {
        _dbContext.SyncStates.Update(state);
        await _dbContext.SaveChangesAsync(ct);
    }
}
