namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>The v1 action allowlist (spec 4.3). New types are additive: enum value, payload schema, renderer, executor.</summary>
public enum AdActionType
{
    AddNegativeKeyword = 1,
    PauseAd = 2,
}
