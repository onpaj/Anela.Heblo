namespace Anela.Heblo.Domain.Features.Campaigns;

public interface ICampaignRepository
{
    // Upsert operations
    Task UpsertCampaignAsync(AdCampaign campaign, CancellationToken ct = default);
    Task UpsertAdSetAsync(AdAdSet adSet, CancellationToken ct = default);
    Task UpsertAdAsync(Ad ad, CancellationToken ct = default);
    Task UpsertDailyMetricAsync(AdDailyMetric metric, CancellationToken ct = default);

    // Sync log
    Task AddSyncLogAsync(AdSyncLog log, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    // Query stubs (implemented in plan 4)
    Task<List<AdCampaign>> GetCampaignsByPlatformAsync(AdPlatform platform, CancellationToken ct = default);
    Task<List<AdDailyMetric>> GetMetricsByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);
}
