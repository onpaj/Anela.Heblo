namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Per platform × action type autonomy (spec 6.4). Seeded ProposeOnly everywhere.</summary>
public enum AdAutonomyMode
{
    ProposeOnly = 1,
    AutoWithinLimits = 2,
    Auto = 3,
}
