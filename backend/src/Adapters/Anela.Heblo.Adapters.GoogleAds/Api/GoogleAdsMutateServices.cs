namespace Anela.Heblo.Adapters.GoogleAds.Api;

/// <summary>The only mutate services Heblo may call (v1 actions: negative keywords, ad pause).</summary>
internal static class GoogleAdsMutateServices
{
    public const string CampaignCriteria = "campaignCriteria";
    public const string AdGroupCriteria = "adGroupCriteria";
    public const string AdGroupAds = "adGroupAds";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { CampaignCriteria, AdGroupCriteria, AdGroupAds };
}
