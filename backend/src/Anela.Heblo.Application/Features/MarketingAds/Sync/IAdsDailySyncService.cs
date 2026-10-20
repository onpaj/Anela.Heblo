namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public interface IAdsDailySyncService
{
    Task<AdsSyncReport> RunAsync(CancellationToken ct);
}
