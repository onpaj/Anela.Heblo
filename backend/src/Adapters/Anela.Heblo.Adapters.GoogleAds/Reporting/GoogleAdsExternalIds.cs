namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

/// <summary>Heblo external-id conventions for Google Ads entities (integration doc §6). Shared by entity and fact mappers.</summary>
internal static class GoogleAdsExternalIds
{
    public static string Keyword(string adGroupId, string criterionId) => $"{adGroupId}~{criterionId}";

    public static string Ad(string adGroupId, string adId) => $"{adGroupId}~{adId}";

    public static string AdGroupNegativeKeyword(string adGroupId, string criterionId) =>
        GoogleAdsMappings.NegativeKeywordId(GoogleAdsMappings.AdGroupCriteriaCollection, $"{adGroupId}~{criterionId}");

    public static string CampaignNegativeKeyword(string campaignId, string criterionId) =>
        GoogleAdsMappings.NegativeKeywordId(GoogleAdsMappings.CampaignCriteriaCollection, $"{campaignId}~{criterionId}");
}
