using Anela.Heblo.Persistence.ShoptetOrders;
using Anela.Heblo.Persistence.ShoptetOrders.Entities;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

public sealed class ShoptetSyncWatermarkRepository : IShoptetSyncWatermarkRepository
{
    private readonly ShoptetOrdersDbContext _dbContext;

    public ShoptetSyncWatermarkRepository(ShoptetOrdersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ShoptetSyncState> GetOrCreateAsync(string entityName, CancellationToken ct = default)
    {
        var state = await _dbContext.SyncStates.FindAsync([entityName], ct);
        if (state != null)
            return state;

        state = new ShoptetSyncState { EntityName = entityName };
        _dbContext.SyncStates.Add(state);
        await _dbContext.SaveChangesAsync(ct);
        return state;
    }

    public async Task SaveAsync(ShoptetSyncState state, CancellationToken ct = default)
    {
        _dbContext.SyncStates.Update(state);
        await _dbContext.SaveChangesAsync(ct);
    }
}
