using Anela.Heblo.Domain.Features.Campaigns.Dtos;

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

    // Query methods
    Task<IReadOnlyList<AdCampaign>> GetCampaignsByPlatformAsync(AdPlatform platform, CancellationToken ct = default);
    Task<IReadOnlyList<AdDailyMetric>> GetMetricsByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<CampaignDashboardDto> GetDashboardAsync(DateOnly from, DateOnly to, AdPlatform? platform, CancellationToken ct = default);
    Task<IReadOnlyList<CampaignSummaryDto>> GetCampaignListAsync(DateOnly from, DateOnly to, AdPlatform? platform, CancellationToken ct = default);
    Task<CampaignDetailDto> GetCampaignDetailAsync(Guid campaignId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
