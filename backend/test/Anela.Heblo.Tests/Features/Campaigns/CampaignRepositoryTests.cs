using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Campaigns;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Campaigns;

public class CampaignRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly CampaignRepository _repository;

    public CampaignRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"CampaignTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new CampaignRepository(_context);
    }

    // --- UpsertCampaignAsync ---

    [Fact]
    public async Task UpsertCampaignAsync_WhenNew_AddsCampaign()
    {
        var campaign = new AdCampaign
        {
            Platform = AdPlatform.Meta,
            PlatformCampaignId = "meta-001",
            Name = "Test Campaign"
        };

        await _repository.UpsertCampaignAsync(campaign);
        await _repository.SaveChangesAsync();

        var saved = await _context.AdCampaigns.SingleAsync();
        Assert.Equal("meta-001", saved.PlatformCampaignId);
        Assert.Equal("Test Campaign", saved.Name);
        Assert.NotEqual(Guid.Empty, saved.Id);
    }

    [Fact]
    public async Task UpsertCampaignAsync_WhenExisting_UpdatesFields()
    {
        var campaign = new AdCampaign
        {
            Platform = AdPlatform.Meta,
            PlatformCampaignId = "meta-001",
            Name = "Original"
        };
        await _repository.UpsertCampaignAsync(campaign);
        await _repository.SaveChangesAsync();

        var update = new AdCampaign
        {
            Platform = AdPlatform.Meta,
            PlatformCampaignId = "meta-001",
            Name = "Updated",
            Status = "PAUSED"
        };
        await _repository.UpsertCampaignAsync(update);
        await _repository.SaveChangesAsync();

        var campaigns = await _context.AdCampaigns.ToListAsync();
        Assert.Single(campaigns);
        Assert.Equal("Updated", campaigns[0].Name);
        Assert.Equal("PAUSED", campaigns[0].Status);
    }

    // --- UpsertAdSetAsync ---

    [Fact]
    public async Task UpsertAdSetAsync_WhenNew_AddsAdSet()
    {
        var campaignId = Guid.NewGuid();
        var adSet = new AdAdSet
        {
            CampaignId = campaignId,
            PlatformAdSetId = "adset-001",
            Name = "Test AdSet"
        };

        await _repository.UpsertAdSetAsync(adSet);
        await _repository.SaveChangesAsync();

        var saved = await _context.AdAdSets.SingleAsync();
        Assert.Equal("adset-001", saved.PlatformAdSetId);
        Assert.NotEqual(Guid.Empty, saved.Id);
    }

    [Fact]
    public async Task UpsertAdSetAsync_WhenExisting_UpdatesFields()
    {
        var campaignId = Guid.NewGuid();
        var adSet = new AdAdSet { CampaignId = campaignId, PlatformAdSetId = "adset-001", Name = "Original" };
        await _repository.UpsertAdSetAsync(adSet);
        await _repository.SaveChangesAsync();

        var update = new AdAdSet { CampaignId = campaignId, PlatformAdSetId = "adset-001", Name = "Updated", Status = "PAUSED" };
        await _repository.UpsertAdSetAsync(update);
        await _repository.SaveChangesAsync();

        var adSets = await _context.AdAdSets.ToListAsync();
        Assert.Single(adSets);
        Assert.Equal("Updated", adSets[0].Name);
        Assert.Equal("PAUSED", adSets[0].Status);
    }

    // --- UpsertAdAsync ---

    [Fact]
    public async Task UpsertAdAsync_WhenNew_AddsAd()
    {
        var adSetId = Guid.NewGuid();
        var ad = new Ad { AdSetId = adSetId, PlatformAdId = "ad-001", Name = "Test Ad" };

        await _repository.UpsertAdAsync(ad);
        await _repository.SaveChangesAsync();

        var saved = await _context.Ads.SingleAsync();
        Assert.Equal("ad-001", saved.PlatformAdId);
        Assert.NotEqual(Guid.Empty, saved.Id);
    }

    // --- UpsertDailyMetricAsync ---

    [Fact]
    public async Task UpsertDailyMetricAsync_WhenNew_AddsMetric()
    {
        var adId = Guid.NewGuid();
        var date = new DateTime(2025, 1, 1);
        var metric = new AdDailyMetric { AdId = adId, Date = date, Impressions = 1000, Clicks = 50, Spend = 10m };

        await _repository.UpsertDailyMetricAsync(metric);
        await _repository.SaveChangesAsync();

        var saved = await _context.AdDailyMetrics.SingleAsync();
        Assert.Equal(adId, saved.AdId);
        Assert.Equal(1000, saved.Impressions);
    }

    [Fact]
    public async Task UpsertDailyMetricAsync_WhenExisting_UpdatesMetrics()
    {
        var adId = Guid.NewGuid();
        var date = new DateTime(2025, 1, 1);
        var metric = new AdDailyMetric { AdId = adId, Date = date, Impressions = 1000, Clicks = 50, Spend = 10m };
        await _repository.UpsertDailyMetricAsync(metric);
        await _repository.SaveChangesAsync();

        var update = new AdDailyMetric { AdId = adId, Date = date, Impressions = 2000, Clicks = 100, Spend = 20m };
        await _repository.UpsertDailyMetricAsync(update);
        await _repository.SaveChangesAsync();

        var metrics = await _context.AdDailyMetrics.ToListAsync();
        Assert.Single(metrics);
        Assert.Equal(2000, metrics[0].Impressions);
        Assert.Equal(100, metrics[0].Clicks);
    }

    // --- AddSyncLogAsync ---

    [Fact]
    public async Task AddSyncLogAsync_AddsLog()
    {
        var log = new AdSyncLog
        {
            Platform = AdPlatform.Meta,
            Status = AdSyncStatus.Running,
            StartedAt = DateTime.UtcNow
        };

        await _repository.AddSyncLogAsync(log);
        await _repository.SaveChangesAsync();

        var saved = await _context.AdSyncLogs.SingleAsync();
        Assert.Equal(AdPlatform.Meta, saved.Platform);
        Assert.NotEqual(Guid.Empty, saved.Id);
    }

    // --- GetCampaignsByPlatformAsync ---

    [Fact]
    public async Task GetCampaignsByPlatformAsync_ReturnsOnlyMatchingPlatform()
    {
        var meta = new AdCampaign { Platform = AdPlatform.Meta, PlatformCampaignId = "m1", Name = "Meta Campaign" };
        var google = new AdCampaign { Platform = AdPlatform.Google, PlatformCampaignId = "g1", Name = "Google Campaign" };
        await _repository.UpsertCampaignAsync(meta);
        await _repository.UpsertCampaignAsync(google);
        await _repository.SaveChangesAsync();

        var result = await _repository.GetCampaignsByPlatformAsync(AdPlatform.Meta);

        Assert.Single(result);
        Assert.Equal("Meta Campaign", result[0].Name);
    }

    // --- GetMetricsByDateRangeAsync ---

    [Fact]
    public async Task GetMetricsByDateRangeAsync_ReturnsOnlyMetricsInRange()
    {
        var adId = Guid.NewGuid();
        var inRange1 = new AdDailyMetric { AdId = adId, Date = new DateTime(2025, 1, 10), Impressions = 100 };
        var inRange2 = new AdDailyMetric { AdId = adId, Date = new DateTime(2025, 1, 15), Impressions = 200 };
        var outOfRange = new AdDailyMetric { AdId = adId, Date = new DateTime(2025, 2, 1), Impressions = 300 };

        await _repository.UpsertDailyMetricAsync(inRange1);
        await _repository.UpsertDailyMetricAsync(inRange2);
        await _repository.UpsertDailyMetricAsync(outOfRange);
        await _repository.SaveChangesAsync();

        var result = await _repository.GetMetricsByDateRangeAsync(
            new DateTime(2025, 1, 1),
            new DateTime(2025, 1, 31));

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, m => m.Impressions == 300);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
