using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

internal static class GoogleAdsFactMapper
{
    private sealed record Metrics(long Impressions, long Clicks, decimal Cost, decimal Conversions, decimal ConversionValue);

    public static AdDailyFactRow Fact(AdEntityLevel level, JsonElement row, string currency)
    {
        var m = ReadMetrics(row);
        return new AdDailyFactRow(
            level, EntityId(level, row), Date(row),
            m.Impressions, m.Clicks, m.Cost, m.Conversions, m.ConversionValue, currency);
    }

    public static AdSearchTermRow SearchTerm(JsonElement row, string currency)
    {
        var m = ReadMetrics(row);
        return new AdSearchTermRow(
            GoogleAdsJson.RequiredString(row, "adGroup", "id"),
            Date(row),
            GoogleAdsJson.RequiredString(row, "searchTermView", "searchTerm"),
            GoogleAdsMappings.SearchTermMatch(GoogleAdsJson.String(row, "segments", "searchTermMatchType")),
            m.Impressions, m.Clicks, m.Cost, m.Conversions, m.ConversionValue, currency);
    }

    /// <summary>
    /// Google reports EXACT and NEAR_EXACT (PHRASE and NEAR_PHRASE) as separate rows. Both map to one
    /// KeywordMatchType, i.e. one natural key in ad_search_term_daily, so they are summed here.
    /// </summary>
    public static IReadOnlyList<AdSearchTermRow> MergeDuplicates(IEnumerable<AdSearchTermRow> rows) =>
        rows.GroupBy(r => (r.AdGroupExternalId, r.Date, r.SearchTerm, r.MatchType))
            .Select(group => group.Skip(1).Aggregate(group.First(), (sum, r) => sum with
            {
                Impressions = sum.Impressions + r.Impressions,
                Clicks = sum.Clicks + r.Clicks,
                Cost = sum.Cost + r.Cost,
                Conversions = sum.Conversions + r.Conversions,
                ConversionValue = sum.ConversionValue + r.ConversionValue,
            }))
            .ToList();

    // Google omits metrics that are zero (proto3 defaults); the JsonHelpers read a missing value as 0.
    private static Metrics ReadMetrics(JsonElement row) => new(
        GoogleAdsJson.Int64(row, "metrics", "impressions"),
        GoogleAdsJson.Int64(row, "metrics", "clicks"),
        GoogleAdsJson.Micros(row, "metrics", "costMicros"),
        GoogleAdsJson.Decimal(row, "metrics", "conversions"),
        GoogleAdsJson.Decimal(row, "metrics", "conversionsValue"));

    private static string EntityId(AdEntityLevel level, JsonElement row) => level switch
    {
        AdEntityLevel.Campaign => GoogleAdsJson.RequiredString(row, "campaign", "id"),
        AdEntityLevel.AdGroup => GoogleAdsJson.RequiredString(row, "adGroup", "id"),
        AdEntityLevel.Keyword => GoogleAdsExternalIds.Keyword(
            GoogleAdsJson.RequiredString(row, "adGroup", "id"),
            GoogleAdsJson.RequiredString(row, "adGroupCriterion", "criterionId")),
        AdEntityLevel.Ad => GoogleAdsExternalIds.Ad(
            GoogleAdsJson.RequiredString(row, "adGroup", "id"),
            GoogleAdsJson.RequiredString(row, "adGroupAd", "ad", "id")),
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Google Ads reports facts per campaign, ad group, keyword and ad."),
    };

    private static DateOnly Date(JsonElement row) =>
        DateOnly.ParseExact(GoogleAdsJson.RequiredString(row, "segments", "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
