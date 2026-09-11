using Anela.Heblo.Domain.Features.Campaigns;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Campaigns;

public class SyncMetaAdsHandler : AdSyncHandlerBase, IRequestHandler<SyncMetaAdsRequest>
{
    private readonly IMetaAdsClient _metaAdsClient;
    private readonly ICampaignRepository _repository;
    private readonly ILogger<SyncMetaAdsHandler> _logger;

    private int _maxCampaigns = 50;
    private int _syncDaysBack = 30;

    public SyncMetaAdsHandler(
        IMetaAdsClient metaAdsClient,
        ICampaignRepository repository,
        ILogger<SyncMetaAdsHandler> logger)
    {
        _metaAdsClient = metaAdsClient;
        _repository = repository;
        _logger = logger;
    }

    protected override AdPlatform Platform => AdPlatform.Meta;

    public async Task Handle(SyncMetaAdsRequest request, CancellationToken cancellationToken)
    {
        _maxCampaigns = request.MaxCampaigns;
        _syncDaysBack = request.SyncDaysBack;
        await ExecuteSyncAsync(_repository, _logger, cancellationToken);
    }

    protected override async Task<(int Campaigns, int AdSets, int Ads, int Metrics)> SyncAsync(
        CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow.Date.AddDays(-_syncDaysBack);
        var until = DateTime.UtcNow.Date;

        int campaignCount = 0, adSetCount = 0, adCount = 0, metricCount = 0;

        var allCampaigns = await _metaAdsClient.GetCampaignsAsync(cancellationToken);
        var campaigns = allCampaigns.Take(_maxCampaigns).ToList();
        _logger.LogInformation(
            "MetaAds sync: fetched {Total} campaigns, processing {Processing} (limit: {Limit})",
            allCampaigns.Count, campaigns.Count, _maxCampaigns);

        foreach (var campaignDto in campaigns)
        {
            var campaign = new AdCampaign
            {
                Id = Guid.NewGuid(),
                Platform = AdPlatform.Meta,
                PlatformCampaignId = campaignDto.Id,
                Name = campaignDto.Name,
                Status = campaignDto.Status,
                Objective = campaignDto.Objective,
                DailyBudget = campaignDto.DailyBudget,
                LifetimeBudget = campaignDto.LifetimeBudget,
                StartDate = campaignDto.StartTime,
                EndDate = campaignDto.StopTime,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            await _repository.UpsertCampaignAsync(campaign, cancellationToken);
            campaignCount++;

            var adSets = await _metaAdsClient.GetAdSetsAsync(campaignDto.Id, cancellationToken);

            foreach (var adSetDto in adSets)
            {
                var adSet = new AdAdSet
                {
                    Id = Guid.NewGuid(),
                    CampaignId = campaign.Id,
                    PlatformAdSetId = adSetDto.Id,
                    Name = adSetDto.Name,
                    Status = adSetDto.Status,
                    DailyBudget = adSetDto.DailyBudget,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };

                await _repository.UpsertAdSetAsync(adSet, cancellationToken);
                adSetCount++;

                var ads = await _metaAdsClient.GetAdsAsync(adSetDto.Id, cancellationToken);

                foreach (var adDto in ads)
                {
                    var ad = new Ad
                    {
                        Id = Guid.NewGuid(),
                        AdSetId = adSet.Id,
                        PlatformAdId = adDto.Id,
                        Name = adDto.Name,
                        Status = adDto.Status,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    };

                    await _repository.UpsertAdAsync(ad, cancellationToken);
                    adCount++;

                    var insights = await _metaAdsClient.GetInsightsAsync(adDto.Id, since, until, cancellationToken);

                    foreach (var insight in insights)
                    {
                        var metric = new AdDailyMetric
                        {
                            Id = Guid.NewGuid(),
                            AdId = ad.Id,
                            Date = insight.Date,
                            Impressions = insight.Impressions,
                            Clicks = insight.Clicks,
                            Spend = insight.Spend,
                            Revenue = insight.Revenue,
                            Conversions = insight.Conversions,
                            CreatedAt = DateTime.UtcNow,
                        };

                        await _repository.UpsertDailyMetricAsync(metric, cancellationToken);
                        metricCount++;
                    }
                }
            }
        }

        return (campaignCount, adSetCount, adCount, metricCount);
    }
}
