namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

/// <summary>Derived ratios; null when the denominator is zero. Rounding matches the Metabase views.</summary>
public static class AdMetrics
{
    public static decimal? CtrPercent(long impressions, long clicks) =>
        impressions > 0 ? Math.Round((decimal)clicks * 100 / impressions, 3, MidpointRounding.AwayFromZero) : null;

    public static decimal? AverageCpc(decimal cost, long clicks) =>
        clicks > 0 ? Math.Round(cost / clicks, 2, MidpointRounding.AwayFromZero) : null;

    public static decimal? Roas(decimal value, decimal cost) =>
        cost > 0 ? Math.Round(value / cost, 4, MidpointRounding.AwayFromZero) : null;

    public static decimal? PnoPercent(decimal cost, decimal revenue) =>
        revenue > 0 ? Math.Round(cost * 100 / revenue, 2, MidpointRounding.AwayFromZero) : null;
}
