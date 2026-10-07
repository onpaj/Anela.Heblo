namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>A Meta ad set is an <see cref="AdGroup"/>.</summary>
public enum AdEntityLevel
{
    Campaign = 1,
    AdGroup = 2,
    Keyword = 3,
    NegativeKeyword = 4,
    Ad = 5,
}
