using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

internal static class AdsTestData
{
    public const string AccountId = "111-222-3333";

    /// <summary>06:00 in Prague on 2026-10-07, so "yesterday" is 2026-10-06.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 7, 4, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly Yesterday = new(2026, 10, 6);

    public static readonly IReadOnlyDictionary<string, string?> NoAttributes = new Dictionary<string, string?>();

    public static AdAccountSnapshot Account(string id = AccountId, string currency = "CZK") =>
        new(id, $"Account {id}", currency, "Europe/Prague");

    public static AdEntitySnapshot Campaign(string id, AdEntityStatus status = AdEntityStatus.Enabled,
        IReadOnlyDictionary<string, string?>? attributes = null) =>
        new(AdEntityLevel.Campaign, id, null, null, $"Campaign {id}", status, attributes ?? NoAttributes);

    public static AdEntitySnapshot AdGroup(string id, string campaignId, AdEntityStatus status = AdEntityStatus.Enabled) =>
        new(AdEntityLevel.AdGroup, id, AdEntityLevel.Campaign, campaignId, $"Ad group {id}", status, NoAttributes);

    public static AdEntitySnapshot Ad(string id, string adGroupId, AdEntityStatus status = AdEntityStatus.Enabled) =>
        new(AdEntityLevel.Ad, id, AdEntityLevel.AdGroup, adGroupId, $"Ad {id}", status, NoAttributes);

    public static AdDailyFactRow Fact(AdEntityLevel level, string id, DateOnly date, decimal cost,
        long clicks = 10, long impressions = 100, decimal conversions = 1m, decimal value = 500m, string currency = "CZK") =>
        new(level, id, date, impressions, clicks, cost, conversions, value, currency);

    public static AdSearchTermRow Term(string adGroupId, DateOnly date, string term, decimal cost,
        KeywordMatchType? matchType = KeywordMatchType.Exact) =>
        new(adGroupId, date, term, matchType, 50, 5, cost, 0m, 0m, "CZK");

    public static AdChangeEventRow Change(string eventId, DateTimeOffset at, AdEntityLevel? level, string? entityId,
        string? newValueJson, string? actor = null, AdChangeActorKind kind = AdChangeActorKind.User, string changeType = "StatusChanged") =>
        new(eventId, at, actor, kind, level, entityId, changeType, null, newValueJson);
}
