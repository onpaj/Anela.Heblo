using Anela.Heblo.Domain.Features.Campaigns;
using Google.Ads.GoogleAds;
using Google.Ads.GoogleAds.Config;
using Google.Ads.GoogleAds.Lib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IGoogleAdsClientDomain = Anela.Heblo.Domain.Features.Campaigns.IGoogleAdsClient;

namespace Anela.Heblo.Adapters.GoogleAds;

public class GoogleAdsClientWrapper : IGoogleAdsClientDomain
{
    private readonly IOptionsMonitor<GoogleAdsSettings> _settings;
    private readonly ILogger<GoogleAdsClientWrapper> _logger;

    public GoogleAdsClientWrapper(
        IOptionsMonitor<GoogleAdsSettings> settings,
        ILogger<GoogleAdsClientWrapper> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GoogleCampaignDto>> GetCampaignsAsync(CancellationToken ct = default)
    {
        var (service, customerId) = CreateService();

        var query = """
            SELECT
                campaign.id,
                campaign.name,
                campaign.status,
                campaign.advertising_channel_type,
                campaign_budget.amount_micros,
                campaign.start_date,
                campaign.end_date
            FROM campaign
            WHERE campaign.status != 'REMOVED'
            """;

        var results = new List<GoogleCampaignDto>();

        using var stream = service.SearchStream(customerId, query);
        var responseStream = stream.GetResponseStream();

        while (await responseStream.MoveNextAsync(ct))
        {
            foreach (var row in responseStream.Current.Results)
            {
                var campaign = row.Campaign;
                if (campaign is null)
                {
                    continue;
                }

                DateTime? startDate = null;
                if (DateOnly.TryParseExact(campaign.StartDate, "yyyy-MM-dd", out var startOnly))
                {
                    startDate = startOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                }

                DateTime? endDate = null;
                if (DateOnly.TryParseExact(campaign.EndDate, "yyyy-MM-dd", out var endOnly))
                {
                    endDate = endOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                }

                decimal? dailyBudget = null;
                var budgetMicros = row.CampaignBudget?.AmountMicros;
                if (budgetMicros is not null)
                {
                    dailyBudget = budgetMicros.Value / 1_000_000m;
                }

                results.Add(new GoogleCampaignDto
                {
                    Id = campaign.Id.ToString(),
                    Name = campaign.Name,
                    Status = campaign.Status.ToString(),
                    Objective = campaign.AdvertisingChannelType.ToString(),
                    DailyBudget = dailyBudget,
                    StartDate = startDate,
                    EndDate = endDate,
                });
            }
        }

        _logger.LogInformation("GoogleAds: fetched {Count} campaigns", results.Count);

        return results;
    }

    public async Task<IReadOnlyList<GoogleAdGroupDto>> GetAdGroupsAsync(string campaignId, CancellationToken ct = default)
    {
        var (service, customerId) = CreateService();

        var query = $"""
            SELECT
                ad_group.id,
                ad_group.name,
                ad_group.status,
                ad_group.cpc_bid_micros
            FROM ad_group
            WHERE campaign.id = {campaignId}
              AND ad_group.status != 'REMOVED'
            """;

        var results = new List<GoogleAdGroupDto>();

        using var stream = service.SearchStream(customerId, query);
        var responseStream = stream.GetResponseStream();

        while (await responseStream.MoveNextAsync(ct))
        {
            foreach (var row in responseStream.Current.Results)
            {
                var adGroup = row.AdGroup;
                if (adGroup is null)
                {
                    continue;
                }

                results.Add(new GoogleAdGroupDto
                {
                    Id = adGroup.Id.ToString(),
                    CampaignId = campaignId,
                    Name = adGroup.Name,
                    Status = adGroup.Status.ToString(),
                    CpcBidMicros = (decimal)adGroup.CpcBidMicros,
                });
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<GoogleAdDto>> GetAdsAsync(string adGroupId, CancellationToken ct = default)
    {
        var (service, customerId) = CreateService();

        var query = $"""
            SELECT
                ad_group_ad.ad.id,
                ad_group_ad.ad.name,
                ad_group_ad.status
            FROM ad_group_ad
            WHERE ad_group.id = {adGroupId}
              AND ad_group_ad.status != 'REMOVED'
            """;

        var results = new List<GoogleAdDto>();

        using var stream = service.SearchStream(customerId, query);
        var responseStream = stream.GetResponseStream();

        while (await responseStream.MoveNextAsync(ct))
        {
            foreach (var row in responseStream.Current.Results)
            {
                var adGroupAd = row.AdGroupAd;
                if (adGroupAd?.Ad is null)
                {
                    continue;
                }

                results.Add(new GoogleAdDto
                {
                    Id = adGroupAd.Ad.Id.ToString(),
                    AdGroupId = adGroupId,
                    Name = adGroupAd.Ad.Name,
                    Status = adGroupAd.Status.ToString(),
                });
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<GoogleMetricDto>> GetMetricsAsync(
        string adId,
        DateTime since,
        DateTime until,
        CancellationToken ct = default)
    {
        var (service, customerId) = CreateService();

        var fromStr = since.ToString("yyyy-MM-dd");
        var toStr = until.ToString("yyyy-MM-dd");

        var query = $"""
            SELECT
                segments.date,
                metrics.impressions,
                metrics.clicks,
                metrics.cost_micros,
                metrics.conversions,
                metrics.conversions_value,
                ad_group_ad.ad.id
            FROM ad_group_ad
            WHERE ad_group_ad.ad.id = {adId}
              AND segments.date BETWEEN '{fromStr}' AND '{toStr}'
            """;

        var results = new List<GoogleMetricDto>();

        using var stream = service.SearchStream(customerId, query);
        var responseStream = stream.GetResponseStream();

        while (await responseStream.MoveNextAsync(ct))
        {
            foreach (var row in responseStream.Current.Results)
            {
                var metrics = row.Metrics;
                var segments = row.Segments;
                if (metrics is null || segments is null)
                {
                    continue;
                }

                if (!DateOnly.TryParseExact(segments.Date, "yyyy-MM-dd", out var dateOnly))
                {
                    continue;
                }

                results.Add(new GoogleMetricDto
                {
                    AdId = adId,
                    Date = dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    Impressions = metrics.Impressions,
                    Clicks = metrics.Clicks,
                    CostMicros = (decimal)metrics.CostMicros,
                    ConversionsValue = (decimal)metrics.ConversionsValue,
                    Conversions = (long)metrics.Conversions,
                });
            }
        }

        return results;
    }

    private (Google.Ads.GoogleAds.V18.Services.GoogleAdsServiceClient service, string customerId) CreateService()
    {
        var s = _settings.CurrentValue;
        var customerId = s.CustomerId.Replace("-", "");

        var config = new GoogleAdsConfig
        {
            DeveloperToken = s.DeveloperToken,
            OAuth2ClientId = s.OAuth2ClientId,
            OAuth2ClientSecret = s.OAuth2ClientSecret,
            OAuth2RefreshToken = s.OAuth2RefreshToken,
            LoginCustomerId = customerId,
        };

        var client = new GoogleAdsClient(config);
        var service = client.GetService(Services.V18.GoogleAdsService);

        return (service, customerId);
    }
}
