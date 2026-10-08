namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public sealed record AdSearchTermRow(
    string AdGroupExternalId, DateOnly Date, string SearchTerm, KeywordMatchType? MatchType,
    long Impressions, long Clicks, decimal Cost, decimal Conversions, decimal ConversionValue, string Currency);
