using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads.Entities;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// The single seam between the contract enums and how C1 stores them in the ads schema
/// (the enum member name as text). If C1 stores them differently, only this file changes.
/// </summary>
public static class AdsDbValues
{
    /// <summary>match_type is part of ad_search_term_daily's primary key, so "no match type" cannot be NULL.</summary>
    public const string NoMatchType = AdSearchTermDaily.UnknownMatchType;
    public const string StatusSucceeded = "Succeeded";
    public const string StatusFailed = "Failed";

    public static string ToDb<TEnum>(TEnum value) where TEnum : struct, Enum => value.ToString();

    public static TEnum FromDb<TEnum>(string value) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException($"Unknown {typeof(TEnum).Name} value '{value}' in the ads schema.");

    public static string MatchTypeToDb(KeywordMatchType? matchType) => matchType?.ToString() ?? NoMatchType;

    public static KeywordMatchType? MatchTypeFromDb(string value) =>
        value == NoMatchType ? null : FromDb<KeywordMatchType>(value);
}
