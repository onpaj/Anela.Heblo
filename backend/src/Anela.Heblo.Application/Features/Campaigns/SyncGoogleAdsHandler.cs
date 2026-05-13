using Anela.Heblo.Domain.Features.Campaigns;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Campaigns;

public class SyncGoogleAdsHandler : IRequestHandler<SyncGoogleAdsRequest>
{
    private readonly IGoogleAdsClient _googleAdsClient;
    private readonly ICampaignRepository _repository;
    private readonly ILogger<SyncGoogleAdsHandler> _logger;

    public SyncGoogleAdsHandler(
        IGoogleAdsClient googleAdsClient,
        ICampaignRepository repository,
        ILogger<SyncGoogleAdsHandler> logger)
    {
        _googleAdsClient = googleAdsClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task Handle(SyncGoogleAdsRequest request, CancellationToken cancellationToken)
    {
        var syncLog = new AdSyncLog
        {
            Id = Guid.NewGuid(),
            Platform = AdPlatform.Google,
            Status = AdSyncStatus.Running,
            StartedAt = DateTime.UtcNow,
        };

        await _repository.AddSyncLogAsync(syncLog, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        try
        {
            var since = DateTime.UtcNow.Date.AddDays(-7);
            var until = DateTime.UtcNow.Date;

            int campaignCount = 0, adSetCount = 0, adCount = 0, metricCount = 0;

            var campaigns = await _googleAdsClient.GetCampaignsAsync(cancellationToken);
            _logger.LogInformation("GoogleAds sync: fetched {Count} campaigns", campaigns.Count);

            foreach (var campaignDto in campaigns)
            {
                var campaign = new AdCampaign
                {
                    Id = Guid.NewGuid(),
                    Platform = AdPlatform.Google,
                    PlatformCampaignId = campaignDto.Id,
                    Name = campaignDto.Name,
                    Status = campaignDto.Status,
                    Objective = campaignDto.Objective,
                    DailyBudget = campaignDto.DailyBudget,
                    LifetimeBudget = null,
                    StartDate = campaignDto.StartDate,
                    EndDate = campaignDto.EndDate,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };

                await _repository.UpsertCampaignAsync(campaign, cancellationToken);
                campaignCount++;

                var adGroups = await _googleAdsClient.GetAdGroupsAsync(campaignDto.Id, cancellationToken);

                foreach (var adGroupDto in adGroups)
                {
                    var adSet = new AdAdSet
                    {
                        Id = Guid.NewGuid(),
                        CampaignId = campaign.Id,
                        PlatformAdSetId = adGroupDto.Id,
                        Name = adGroupDto.Name,
                        Status = adGroupDto.Status,
                        DailyBudget = adGroupDto.CpcBidMicros.HasValue
                            ? adGroupDto.CpcBidMicros.Value / 1_000_000m
                            : null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    };

                    await _repository.UpsertAdSetAsync(adSet, cancellationToken);
                    adSetCount++;

                    var ads = await _googleAdsClient.GetAdsAsync(adGroupDto.Id, cancellationToken);

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

                        var metrics = await _googleAdsClient.GetMetricsAsync(adDto.Id, since, until, cancellationToken);

                        foreach (var metricDto in metrics)
                        {
                            var metric = new AdDailyMetric
                            {
                                Id = Guid.NewGuid(),
                                AdId = ad.Id,
                                Date = metricDto.Date,
                                Impressions = metricDto.Impressions,
                                Clicks = metricDto.Clicks,
                                Spend = metricDto.CostMicros / 1_000_000m,
                                Revenue = metricDto.ConversionsValue,
                                Conversions = metricDto.Conversions,
                                CreatedAt = DateTime.UtcNow,
                            };

                            await _repository.UpsertDailyMetricAsync(metric, cancellationToken);
                            metricCount++;
                        }
                    }
                }
            }

            await _repository.SaveChangesAsync(cancellationToken);

            syncLog.Complete(campaignCount, adSetCount, adCount, metricCount);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "GoogleAds sync completed: {Campaigns} campaigns, {AdSets} ad sets, {Ads} ads, {Metrics} metric rows",
                campaignCount, adSetCount, adCount, metricCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GoogleAds sync failed");
            syncLog.Fail(ex.Message);
            await _repository.SaveChangesAsync(cancellationToken);
            throw;
        }
    }
}
