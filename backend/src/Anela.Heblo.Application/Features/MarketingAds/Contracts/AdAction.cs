namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// One typed change to an ad account (spec 4.3 / 12.2). Conventions:
/// AddNegativeKeyword → TargetLevel Campaign|AdGroup, OldValue Absent, NewValue Present, payload text + matchType (KeywordMatchType name);
/// PauseAd → TargetLevel Ad, OldValue Enabled, NewValue Paused, empty payload.
/// </summary>
public sealed record AdAction(
    AdActionType Type, AdPlatform Platform, string AccountExternalId,
    AdEntityLevel TargetLevel, string TargetExternalId,
    string OldValue, string NewValue, IReadOnlyDictionary<string, string> Payload);
