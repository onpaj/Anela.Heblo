namespace Anela.Heblo.Adapters.Flexi.Analytics;

public interface IFlexiAnalyticsSyncService
{
    Task<FlexiAnalyticsSyncReport> SyncAllAsync(CancellationToken ct = default);
}
