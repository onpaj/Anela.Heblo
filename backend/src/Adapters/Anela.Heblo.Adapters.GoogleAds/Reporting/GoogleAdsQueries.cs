using System.Globalization;
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

/// <summary>
/// GAQL for the read source. Query names double as fixture file names. Entity queries do not filter
/// REMOVED: facts in the lookback window can belong to an entity removed since.
/// </summary>
internal static class GoogleAdsQueries
{
    public static readonly GoogleAdsQuery Customer = new("customer",
        "SELECT customer.id, customer.descriptive_name, customer.currency_code, customer.time_zone, customer.manager " +
        "FROM customer LIMIT 1");

    public static readonly GoogleAdsQuery Campaigns = new("campaigns",
        "SELECT campaign.id, campaign.name, campaign.status, campaign.advertising_channel_type, " +
        "campaign.bidding_strategy_type, campaign_budget.amount_micros FROM campaign");

    public static readonly GoogleAdsQuery AdGroups = new("ad_groups",
        "SELECT campaign.id, ad_group.id, ad_group.name, ad_group.status, ad_group.type FROM ad_group");

    public static readonly GoogleAdsQuery Keywords = new("keywords",
        "SELECT ad_group.id, ad_group_criterion.criterion_id, ad_group_criterion.status, " +
        "ad_group_criterion.keyword.text, ad_group_criterion.keyword.match_type FROM ad_group_criterion " +
        "WHERE ad_group_criterion.type = 'KEYWORD' AND ad_group_criterion.negative = FALSE");

    public static readonly GoogleAdsQuery AdGroupNegativeKeywords = new("ad_group_negative_keywords",
        "SELECT ad_group.id, ad_group_criterion.criterion_id, ad_group_criterion.status, " +
        "ad_group_criterion.keyword.text, ad_group_criterion.keyword.match_type FROM ad_group_criterion " +
        "WHERE ad_group_criterion.type = 'KEYWORD' AND ad_group_criterion.negative = TRUE");

    public static readonly GoogleAdsQuery CampaignNegativeKeywords = new("campaign_negative_keywords",
        "SELECT campaign.id, campaign_criterion.criterion_id, campaign_criterion.status, " +
        "campaign_criterion.keyword.text, campaign_criterion.keyword.match_type FROM campaign_criterion " +
        "WHERE campaign_criterion.type = 'KEYWORD' AND campaign_criterion.negative = TRUE");

    public static readonly GoogleAdsQuery Ads = new("ads",
        "SELECT ad_group.id, ad_group_ad.ad.id, ad_group_ad.ad.name, ad_group_ad.ad.type, ad_group_ad.status " +
        "FROM ad_group_ad");

    private const string Metrics =
        "metrics.impressions, metrics.clicks, metrics.cost_micros, metrics.conversions, metrics.conversions_value";

    public static IReadOnlyList<(AdEntityLevel Level, GoogleAdsQuery Query)> FactQueries(DateOnly date)
    {
        var day = Day(date);
        return new[]
        {
            (AdEntityLevel.Campaign, new GoogleAdsQuery("campaign_facts",
                $"SELECT campaign.id, segments.date, {Metrics} FROM campaign WHERE segments.date = '{day}'")),
            (AdEntityLevel.AdGroup, new GoogleAdsQuery("ad_group_facts",
                $"SELECT ad_group.id, segments.date, {Metrics} FROM ad_group WHERE segments.date = '{day}'")),
            (AdEntityLevel.Keyword, new GoogleAdsQuery("keyword_facts",
                $"SELECT ad_group.id, ad_group_criterion.criterion_id, segments.date, {Metrics} " +
                $"FROM keyword_view WHERE segments.date = '{day}'")),
            (AdEntityLevel.Ad, new GoogleAdsQuery("ad_facts",
                $"SELECT ad_group.id, ad_group_ad.ad.id, segments.date, {Metrics} " +
                $"FROM ad_group_ad WHERE segments.date = '{day}'")),
        };
    }

    public static GoogleAdsQuery SearchTerms(DateOnly date) => new("search_terms",
        $"SELECT ad_group.id, search_term_view.search_term, segments.search_term_match_type, segments.date, {Metrics} " +
        $"FROM search_term_view WHERE segments.date = '{Day(date)}'");

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
