using Anela.Heblo.Adapters.GoogleAds.Api;

namespace Anela.Heblo.Adapters.GoogleAds.Execution;

/// <summary>Only numeric ids are interpolated (re-checked here); keyword text is matched in C#, never in GAQL.</summary>
internal static class GoogleAdsExecutorQueries
{
    public static GoogleAdsQuery Campaign(string campaignId) => new("executor_campaign",
        $"SELECT campaign.id, campaign.status FROM campaign WHERE campaign.id = {Id(campaignId)}");

    public static GoogleAdsQuery AdGroup(string adGroupId) => new("executor_ad_group",
        $"SELECT ad_group.id, ad_group.status FROM ad_group WHERE ad_group.id = {Id(adGroupId)}");

    public static GoogleAdsQuery CampaignNegatives(string campaignId) => new("executor_campaign_negatives",
        "SELECT campaign_criterion.resource_name, campaign_criterion.keyword.text, campaign_criterion.keyword.match_type " +
        $"FROM campaign_criterion WHERE campaign.id = {Id(campaignId)} AND campaign_criterion.type = 'KEYWORD' " +
        "AND campaign_criterion.negative = TRUE AND campaign_criterion.status != 'REMOVED'");

    public static GoogleAdsQuery AdGroupNegatives(string adGroupId) => new("executor_ad_group_negatives",
        "SELECT ad_group_criterion.resource_name, ad_group_criterion.keyword.text, ad_group_criterion.keyword.match_type " +
        $"FROM ad_group_criterion WHERE ad_group.id = {Id(adGroupId)} AND ad_group_criterion.type = 'KEYWORD' " +
        "AND ad_group_criterion.negative = TRUE AND ad_group_criterion.status != 'REMOVED'");

    public static GoogleAdsQuery AdGroupAd(string adGroupId, string adId) => new("executor_ad_group_ad",
        "SELECT ad_group_ad.resource_name, ad_group_ad.status FROM ad_group_ad " +
        $"WHERE ad_group.id = {Id(adGroupId)} AND ad_group_ad.ad.id = {Id(adId)}");

    private static string Id(string value) => GoogleAdsIds.RequireNumericId(value, nameof(value));
}
