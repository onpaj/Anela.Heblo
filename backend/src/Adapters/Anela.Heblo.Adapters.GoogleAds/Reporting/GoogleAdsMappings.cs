using System.Text.RegularExpressions;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

internal static class GoogleAdsMappings
{
    public const string CampaignCriteriaCollection = "campaignCriteria";
    public const string AdGroupCriteriaCollection = "adGroupCriteria";
    private const string ApiClientType = "GOOGLE_ADS_API";

    private static readonly Regex ResourceName = new(
        @"^customers/\d+/(?<collection>[A-Za-z]+)/(?<id>\d+(~\d+)?)$", RegexOptions.CultureInvariant);

    public static AdEntityStatus Status(string? apiStatus) => apiStatus switch
    {
        "ENABLED" => AdEntityStatus.Enabled,
        "PAUSED" => AdEntityStatus.Paused,
        "REMOVED" => AdEntityStatus.Removed,
        _ => AdEntityStatus.Unknown,
    };

    public static KeywordMatchType? KeywordMatch(string? apiMatchType) => apiMatchType switch
    {
        "EXACT" => KeywordMatchType.Exact,
        "PHRASE" => KeywordMatchType.Phrase,
        "BROAD" => KeywordMatchType.Broad,
        _ => null,
    };

    /// <summary>NEAR_* are Google's close variants; AI_MAX / PERFORMANCE_MAX have no keyword match type.</summary>
    public static KeywordMatchType? SearchTermMatch(string? apiMatchType) => apiMatchType switch
    {
        "EXACT" or "NEAR_EXACT" => KeywordMatchType.Exact,
        "PHRASE" or "NEAR_PHRASE" => KeywordMatchType.Phrase,
        "BROAD" => KeywordMatchType.Broad,
        _ => null,
    };

    public static string ToApiMatchType(KeywordMatchType matchType) => matchType switch
    {
        KeywordMatchType.Exact => "EXACT",
        KeywordMatchType.Phrase => "PHRASE",
        KeywordMatchType.Broad => "BROAD",
        _ => throw new ArgumentOutOfRangeException(nameof(matchType), matchType, "Unknown keyword match type."),
    };

    /// <summary>
    /// Heblo = an API change by Heblo's own Google user. Recommendations, automated rules and Google's
    /// internal tools are the platform acting on its own. Everything a person drives (UI, Editor,
    /// scripts, bulk uploads, other API tools) is a User change.
    /// </summary>
    public static AdChangeActorKind ActorKind(string? clientType, string? userEmail, string? hebloUserEmail)
    {
        if (clientType == ApiClientType
            && !string.IsNullOrWhiteSpace(hebloUserEmail)
            && string.Equals(userEmail, hebloUserEmail, StringComparison.OrdinalIgnoreCase))
            return AdChangeActorKind.Heblo;

        return clientType switch
        {
            "GOOGLE_ADS_RECOMMENDATIONS" or "GOOGLE_ADS_RECOMMENDATIONS_SUBSCRIPTION"
                or "GOOGLE_ADS_AUTOMATED_RULE" or "INTERNAL_TOOL" => AdChangeActorKind.PlatformAutomation,
            "GOOGLE_ADS_WEB_CLIENT" or "GOOGLE_ADS_EDITOR" or "GOOGLE_ADS_MOBILE_APP"
                or "GOOGLE_ADS_BULK_UPLOAD" or "GOOGLE_ADS_SCRIPTS" or ApiClientType => AdChangeActorKind.User,
            _ => AdChangeActorKind.Unknown,
        };
    }

    /// <summary>Maps a change_event resource name to the entity level and Heblo external id (integration doc §6).</summary>
    public static (AdEntityLevel? Level, string? ExternalId) EntityRef(string? resourceName, bool isNegativeCriterion)
    {
        var match = ResourceName.Match(resourceName ?? string.Empty);
        if (!match.Success)
            return (null, null);

        var collection = match.Groups["collection"].Value;
        var id = match.Groups["id"].Value;
        return collection switch
        {
            "campaigns" => (AdEntityLevel.Campaign, id),
            "adGroups" => (AdEntityLevel.AdGroup, id),
            "adGroupAds" => (AdEntityLevel.Ad, id),
            AdGroupCriteriaCollection when isNegativeCriterion => (AdEntityLevel.NegativeKeyword, NegativeKeywordId(collection, id)),
            AdGroupCriteriaCollection => (AdEntityLevel.Keyword, id),
            CampaignCriteriaCollection => (AdEntityLevel.NegativeKeyword, NegativeKeywordId(collection, id)),
            _ => (null, null),
        };
    }

    public static string NegativeKeywordId(string collection, string compositeId) => $"{collection}/{compositeId}";
}
