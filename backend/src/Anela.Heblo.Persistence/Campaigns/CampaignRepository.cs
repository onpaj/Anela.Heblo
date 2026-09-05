using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
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

    public async Task<IReadOnlyList<AdCampaign>> GetCampaignsByPlatformAsync(AdPlatform platform, CancellationToken ct = default)
        => await _context.AdCampaigns
            .Where(c => c.Platform == platform)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AdDailyMetric>> GetMetricsByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => await _context.AdDailyMetrics
            .Where(m => m.Date >= from && m.Date <= to)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<CampaignDashboardDto> GetDashboardAsync(
        DateOnly from, DateOnly to, AdPlatform? platform, CancellationToken ct = default)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt = to.ToDateTime(TimeOnly.MinValue).AddDays(1);

        var rows = await _context.AdDailyMetrics
            .Include(m => m.Ad)
                .ThenInclude(a => a.AdSet)
                    .ThenInclude(s => s.Campaign)
            .Where(m => m.Date >= fromDt && m.Date < toDt)
            .AsNoTracking()
            .ToListAsync(ct);

        if (platform.HasValue)
            rows = rows.Where(m => m.Ad.AdSet.Campaign.Platform == platform.Value).ToList();

        var totalSpend = rows.Sum(m => m.Spend);
        var totalConversions = rows.Sum(m => m.Conversions);
        var totalRevenue = rows.Sum(m => m.Revenue);
        var totalClicks = rows.Sum(m => m.Clicks);

        var avgRoas = totalSpend > 0 ? Math.Round(totalRevenue / totalSpend, 4) : 0m;
        var avgCpc = totalClicks > 0 ? Math.Round(totalSpend / totalClicks, 4) : 0m;

        var spendOverTime = rows
            .GroupBy(m => DateOnly.FromDateTime(m.Date))
            .OrderBy(g => g.Key)
            .Select(g => new DailySpendDto
            {
                Date = g.Key,
                MetaSpend = g.Where(m => m.Ad.AdSet.Campaign.Platform == AdPlatform.Meta).Sum(m => m.Spend),
                GoogleSpend = g.Where(m => m.Ad.AdSet.Campaign.Platform == AdPlatform.Google).Sum(m => m.Spend)
            })
            .ToList();

        return new CampaignDashboardDto
        {
            TotalSpend = totalSpend,
            TotalConversions = totalConversions,
            AvgRoas = avgRoas,
            AvgCpc = avgCpc,
            SpendOverTime = spendOverTime
        };
    }

    public async Task<IReadOnlyList<CampaignSummaryDto>> GetCampaignListAsync(
        DateOnly from, DateOnly to, AdPlatform? platform, CancellationToken ct = default)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt = to.ToDateTime(TimeOnly.MinValue).AddDays(1);

        var query = _context.AdCampaigns
            .Include(c => c.AdSets)
                .ThenInclude(s => s.Ads)
                    .ThenInclude(a => a.DailyMetrics.Where(m => m.Date >= fromDt && m.Date < toDt))
            .AsNoTracking();

        if (platform.HasValue)
            query = query.Where(c => c.Platform == platform.Value);

        var campaigns = await query.ToListAsync(ct);

        return campaigns.Select(c =>
        {
            var metrics = c.AdSets.SelectMany(s => s.Ads).SelectMany(a => a.DailyMetrics).ToList();
            var spend = metrics.Sum(m => m.Spend);
            var revenue = metrics.Sum(m => m.Revenue);

            return new CampaignSummaryDto
            {
                Id = c.Id,
                Name = c.Name,
                Platform = c.Platform,
                Status = c.Status,
                Spend = spend,
                Impressions = metrics.Sum(m => m.Impressions),
                Clicks = metrics.Sum(m => m.Clicks),
                Conversions = metrics.Sum(m => m.Conversions),
                Roas = spend > 0 ? Math.Round(revenue / spend, 4) : 0m
            };
        }).ToList();
    }

    public async Task<CampaignDetailDto> GetCampaignDetailAsync(
        Guid campaignId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt = to.ToDateTime(TimeOnly.MinValue).AddDays(1);

        var campaign = await _context.AdCampaigns
            .Include(c => c.AdSets)
                .ThenInclude(s => s.Ads)
                    .ThenInclude(a => a.DailyMetrics.Where(m => m.Date >= fromDt && m.Date < toDt))
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

        if (campaign is null)
            throw new KeyNotFoundException($"Campaign {campaignId} not found");

        return new CampaignDetailDto
        {
            Id = campaign.Id,
            Name = campaign.Name,
            Platform = campaign.Platform,
            Status = campaign.Status,
            AdSets = campaign.AdSets.Select(s => new AdSetDetailDto
            {
                Id = s.Id,
                Name = s.Name,
                Status = s.Status,
                Ads = s.Ads.Select(a => new AdSummaryDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    Status = a.Status,
                    Spend = a.DailyMetrics.Sum(m => m.Spend),
                    Impressions = a.DailyMetrics.Sum(m => m.Impressions),
                    Clicks = a.DailyMetrics.Sum(m => m.Clicks),
                    Conversions = a.DailyMetrics.Sum(m => m.Conversions)
                }).ToList()
            }).ToList()
        };
    }
}
