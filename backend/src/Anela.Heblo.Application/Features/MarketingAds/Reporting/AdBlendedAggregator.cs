using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

/// <summary>Sums daily rows into periods; ratios are recomputed from the sums (never averaged).</summary>
public static class AdBlendedAggregator
{
    public static List<AdBlendedPeriodDto> ToPeriods(IReadOnlyList<AdBlendedDayRow> rows, BlendedGranularity grain, DateOnly from, DateOnly to) =>
        rows.GroupBy(r => grain == BlendedGranularity.Monthly ? new DateOnly(r.Date.Year, r.Date.Month, 1) : r.Date)
            .OrderBy(g => g.Key)
            .Select(g => Build(g.ToList(), g.Key, IsPartial(g.Key, grain, from, to)))
            .ToList();

    public static AdBlendedPeriodDto Total(IReadOnlyList<AdBlendedDayRow> rows, DateOnly from) => Build(rows, from, false);

    private static bool IsPartial(DateOnly start, BlendedGranularity grain, DateOnly from, DateOnly to) =>
        grain == BlendedGranularity.Monthly && (start < from || start.AddMonths(1).AddDays(-1) > to);

    private static AdBlendedPeriodDto Build(IReadOnlyList<AdBlendedDayRow> rows, DateOnly start, bool isPartial)
    {
        var cost = rows.Sum(r => r.AdCostCzk);
        var revenue = rows.Sum(r => r.RevenueWithoutVatCzk);
        return new AdBlendedPeriodDto
        {
            PeriodStart = start,
            IsPartial = isPartial,
            AdCostCzk = cost,
            GoogleAdsCostCzk = rows.Sum(r => r.GoogleAdsCostCzk),
            MetaAdsCostCzk = rows.Sum(r => r.MetaAdsCostCzk),
            SklikCostCzk = rows.Sum(r => r.SklikCostCzk),
            HasNonCzkAdCost = rows.Any(r => r.HasNonCzkAdCost),
            DaysWithAdCost = rows.Count(r => r.HasAdCostData),
            Orders = rows.Sum(r => r.Orders),
            RevenueWithoutVatCzk = revenue,
            EshopRevenueWithoutVatCzk = rows.Sum(r => r.EshopRevenueWithoutVatCzk),
            BlendedRoas = AdMetrics.Roas(revenue, cost),
            PnoPercent = AdMetrics.PnoPercent(cost, revenue),
        };
    }
}
