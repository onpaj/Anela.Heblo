using Anela.Heblo.Persistence.Ga4;
using Anela.Heblo.Persistence.Ga4.Entities;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Sync;

public sealed class Ga4SyncWatermarkRepository : IGa4SyncWatermarkRepository
{
    private readonly Ga4DbContext _dbContext;

    public Ga4SyncWatermarkRepository(Ga4DbContext dbContext)
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
