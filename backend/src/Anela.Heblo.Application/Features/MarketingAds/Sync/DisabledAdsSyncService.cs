namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Registered when AdsDbContext is not, so both jobs still resolve (and skip) in unconfigured environments.</summary>
public sealed class DisabledAdsSyncService : IAdsDailySyncService, IAdsChangeSyncService
{
    public const string Reason =
        $"AdsDbContext is not registered ({MarketingAdsModule.ConnectionStringKey} is missing or unusable; see the [MarketingAds] startup warning).";

    public Task<AdsSyncReport> RunAsync(CancellationToken ct) => Task.FromResult(AdsSyncReport.Skip(Reason));
}
