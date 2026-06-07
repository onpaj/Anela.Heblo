using Anela.Heblo.Persistence.Analytics.Entities;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public interface ISyncWatermarkRepository
{
    Task<SyncState> GetOrCreateAsync(string entityName, CancellationToken ct = default);
    Task SaveAsync(SyncState state, CancellationToken ct = default);
}
