using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Campaigns;

public class CampaignRepository : ICampaignRepository
{
    private readonly ApplicationDbContext _context;

    public CampaignRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task UpsertCampaignAsync(AdCampaign campaign, CancellationToken ct = default)
    {
        var existing = await _context.AdCampaigns
            .FirstOrDefaultAsync(c => c.Platform == campaign.Platform && c.PlatformCampaignId == campaign.PlatformCampaignId, ct);

        if (existing is null)
        {
            campaign.Id = Guid.NewGuid();
            campaign.CreatedAt = DateTime.UtcNow;
            campaign.UpdatedAt = DateTime.UtcNow;
            _context.AdCampaigns.Add(campaign);
        }
        else
        {
            existing.Name = campaign.Name;
            existing.Status = campaign.Status;
            existing.Objective = campaign.Objective;
            existing.DailyBudget = campaign.DailyBudget;
            existing.LifetimeBudget = campaign.LifetimeBudget;
            existing.StartDate = campaign.StartDate;
            existing.EndDate = campaign.EndDate;
            existing.UpdatedAt = DateTime.UtcNow;
            campaign.Id = existing.Id;
        }
    }

    public async Task UpsertAdSetAsync(AdAdSet adSet, CancellationToken ct = default)
    {
        var existing = await _context.AdAdSets
            .FirstOrDefaultAsync(a => a.PlatformAdSetId == adSet.PlatformAdSetId, ct);

        if (existing is null)
        {
            adSet.Id = Guid.NewGuid();
            adSet.CreatedAt = DateTime.UtcNow;
            adSet.UpdatedAt = DateTime.UtcNow;
            _context.AdAdSets.Add(adSet);
        }
        else
        {
            existing.CampaignId = adSet.CampaignId;
            existing.Name = adSet.Name;
            existing.Status = adSet.Status;
            existing.DailyBudget = adSet.DailyBudget;
            existing.UpdatedAt = DateTime.UtcNow;
            adSet.Id = existing.Id;
        }
    }

    public async Task UpsertAdAsync(Ad ad, CancellationToken ct = default)
    {
        var existing = await _context.Ads
            .FirstOrDefaultAsync(a => a.PlatformAdId == ad.PlatformAdId, ct);

        if (existing is null)
        {
            ad.Id = Guid.NewGuid();
            ad.CreatedAt = DateTime.UtcNow;
            ad.UpdatedAt = DateTime.UtcNow;
            _context.Ads.Add(ad);
        }
        else
        {
            existing.AdSetId = ad.AdSetId;
            existing.Name = ad.Name;
            existing.Status = ad.Status;
            existing.UpdatedAt = DateTime.UtcNow;
            ad.Id = existing.Id;
        }
    }

    public async Task UpsertDailyMetricAsync(AdDailyMetric metric, CancellationToken ct = default)
    {
        var existing = await _context.AdDailyMetrics
            .FirstOrDefaultAsync(m => m.AdId == metric.AdId && m.Date == metric.Date, ct);

        if (existing is null)
        {
            metric.Id = Guid.NewGuid();
            metric.CreatedAt = DateTime.UtcNow;
            _context.AdDailyMetrics.Add(metric);
        }
        else
        {
            existing.Impressions = metric.Impressions;
            existing.Clicks = metric.Clicks;
            existing.Spend = metric.Spend;
            existing.Revenue = metric.Revenue;
            existing.Conversions = metric.Conversions;
        }
    }

    public Task AddSyncLogAsync(AdSyncLog log, CancellationToken ct = default)
    {
        log.Id = Guid.NewGuid();
        _context.AdSyncLogs.Add(log);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);

    public Task<List<AdCampaign>> GetCampaignsByPlatformAsync(AdPlatform platform, CancellationToken ct = default)
        => _context.AdCampaigns
            .Where(c => c.Platform == platform)
            .ToListAsync(ct);

    public Task<List<AdDailyMetric>> GetMetricsByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => _context.AdDailyMetrics
            .Where(m => m.Date >= from && m.Date <= to)
            .ToListAsync(ct);
}
