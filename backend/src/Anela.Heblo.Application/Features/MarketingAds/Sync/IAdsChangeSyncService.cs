namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public interface IAdsChangeSyncService
{
    Task<AdsSyncReport> RunAsync(CancellationToken ct);
}
