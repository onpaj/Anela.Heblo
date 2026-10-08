using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

/// <summary>Maps entity query rows to snapshots using the external-id conventions of integration doc §6.</summary>
internal static class GoogleAdsEntityMapper
{
    public static AdEntitySnapshot Campaign(JsonElement row) => new(
        AdEntityLevel.Campaign,
        GoogleAdsJson.RequiredString(row, "campaign", "id"),
        null,
        null,
        GoogleAdsJson.String(row, "campaign", "name") ?? string.Empty,
        GoogleAdsMappings.Status(GoogleAdsJson.String(row, "campaign", "status")),
        new Dictionary<string, string?>
        {
            ["advertisingChannelType"] = GoogleAdsJson.String(row, "campaign", "advertisingChannelType"),
            ["biddingStrategyType"] = GoogleAdsJson.String(row, "campaign", "biddingStrategyType"),
            ["budgetAmount"] = GoogleAdsJson.Find(row, "campaignBudget", "amountMicros") is null
                ? null
                : GoogleAdsJson.Micros(row, "campaignBudget", "amountMicros").ToString(CultureInfo.InvariantCulture),
        });

    public static AdEntitySnapshot AdGroup(JsonElement row) => new(
        AdEntityLevel.AdGroup,
        GoogleAdsJson.RequiredString(row, "adGroup", "id"),
        AdEntityLevel.Campaign,
        GoogleAdsJson.RequiredString(row, "campaign", "id"),
        GoogleAdsJson.String(row, "adGroup", "name") ?? string.Empty,
        GoogleAdsMappings.Status(GoogleAdsJson.String(row, "adGroup", "status")),
        new Dictionary<string, string?> { ["adGroupType"] = GoogleAdsJson.String(row, "adGroup", "type") });

    public static AdEntitySnapshot Keyword(JsonElement row)
    {
        var adGroupId = GoogleAdsJson.RequiredString(row, "adGroup", "id");
        return Criterion(row, "adGroupCriterion", AdEntityLevel.Keyword,
            GoogleAdsExternalIds.Keyword(adGroupId, GoogleAdsJson.RequiredString(row, "adGroupCriterion", "criterionId")),
            AdEntityLevel.AdGroup, adGroupId);
    }

    public static AdEntitySnapshot AdGroupNegativeKeyword(JsonElement row)
    {
        var adGroupId = GoogleAdsJson.RequiredString(row, "adGroup", "id");
        return Criterion(row, "adGroupCriterion", AdEntityLevel.NegativeKeyword,
            GoogleAdsExternalIds.AdGroupNegativeKeyword(adGroupId, GoogleAdsJson.RequiredString(row, "adGroupCriterion", "criterionId")),
            AdEntityLevel.AdGroup, adGroupId);
    }

    public static AdEntitySnapshot CampaignNegativeKeyword(JsonElement row)
    {
        var campaignId = GoogleAdsJson.RequiredString(row, "campaign", "id");
        return Criterion(row, "campaignCriterion", AdEntityLevel.NegativeKeyword,
            GoogleAdsExternalIds.CampaignNegativeKeyword(campaignId, GoogleAdsJson.RequiredString(row, "campaignCriterion", "criterionId")),
            AdEntityLevel.Campaign, campaignId);
    }

    public static AdEntitySnapshot Ad(JsonElement row)
    {
        var adGroupId = GoogleAdsJson.RequiredString(row, "adGroup", "id");
        var adId = GoogleAdsJson.RequiredString(row, "adGroupAd", "ad", "id");
        var adType = GoogleAdsJson.String(row, "adGroupAd", "ad", "type");
        var name = GoogleAdsJson.String(row, "adGroupAd", "ad", "name");
        return new AdEntitySnapshot(
            AdEntityLevel.Ad,
            GoogleAdsExternalIds.Ad(adGroupId, adId),
            AdEntityLevel.AdGroup,
            adGroupId,
            string.IsNullOrWhiteSpace(name) ? $"{adType ?? "AD"} {adId}" : name,
            GoogleAdsMappings.Status(GoogleAdsJson.String(row, "adGroupAd", "status")),
            new Dictionary<string, string?> { ["adType"] = adType });
    }

    private static AdEntitySnapshot Criterion(
        JsonElement row, string criterionProperty, AdEntityLevel level, string externalId,
        AdEntityLevel parentLevel, string parentId)
    {
        var text = GoogleAdsJson.String(row, criterionProperty, "keyword", "text") ?? string.Empty;
        return new AdEntitySnapshot(
            level,
            externalId,
            parentLevel,
            parentId,
            text,
            GoogleAdsMappings.Status(GoogleAdsJson.String(row, criterionProperty, "status")),
            new Dictionary<string, string?>
            {
                [AdActionPayloadKeys.Text] = text,
                [AdActionPayloadKeys.MatchType] = GoogleAdsMappings
                    .KeywordMatch(GoogleAdsJson.String(row, criterionProperty, "keyword", "matchType"))?.ToString(),
            });
    }
}
