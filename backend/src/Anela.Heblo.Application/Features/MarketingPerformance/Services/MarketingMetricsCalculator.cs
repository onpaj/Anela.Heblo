using Anela.Heblo.Application.Features.MarketingPerformance.Contracts;
using Anela.Heblo.Domain.Features.MarketingPerformance;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

/// <summary>
/// Pure derivation of the spreadsheet's ratio columns from stored sums.
/// Never stores anything; a changed VAT rate or formula needs no backfill.
/// </summary>
public class MarketingMetricsCalculator
{
    private const decimal Percent = 100m;
    private readonly decimal _vatRate;
    private readonly IReadOnlyList<MarketingChannelDefinition> _channels;

    public MarketingMetricsCalculator(decimal vatRate, IReadOnlyList<MarketingChannelDefinition> channels)
    {
        if (vatRate <= 1m) throw new ArgumentOutOfRangeException(nameof(vatRate), "VAT rate must be > 1, e.g. 1.21.");
        _vatRate = vatRate;
        _channels = channels;
    }

    public MonthlyMarketingPerformanceDto Build(
        MarketingPerformanceMonth month,
        MarketingPerformanceMonth? sameMonthLastYear,
        bool includeWholesale,
        bool isPartial)
    {
        var orders = Orders(month, includeWholesale);
        var revenueWithVat = RevenueWithVat(month, includeWholesale);
        var revenueWithoutVat = revenueWithVat / _vatRate;
        var channelCosts = ChannelCosts(month);
        var totalCost = channelCosts.Sum(c => c.CostWithoutVat);

        return new MonthlyMarketingPerformanceDto
        {
            Year = month.Year,
            Month = month.Month,
            MonthYearDisplay = Display(month.Key),
            HasData = true,
            IsLocked = month.IsLocked,
            IsPartial = isPartial,
            Orders = orders,
            RevenueWithVat = revenueWithVat,
            RevenueWithoutVat = revenueWithoutVat,
            ChannelCosts = channelCosts,
            TotalCost = totalCost,
            Pno = Ratio(totalCost * Percent, revenueWithoutVat),
            // Null when cost is 0 (via Ratio's denominator check) or when revenue is 0 — a month with no revenue has no meaningful return to report.
            Roas = revenueWithoutVat == 0m ? null : Ratio(revenueWithoutVat * Percent, totalCost),
            Profit = revenueWithoutVat - totalCost,
            AvgOrderValue = Ratio(revenueWithoutVat, orders),
            CostPerOrder = Ratio(totalCost, orders),
            YoyCostPercent = PercentOfLastYear(totalCost, sameMonthLastYear is null ? null : TotalCost(sameMonthLastYear)),
            YoyRevenuePercent = PercentOfLastYear(revenueWithVat, sameMonthLastYear is null ? null : RevenueWithVat(sameMonthLastYear, includeWholesale)),
            YoyOrdersPercent = PercentOfLastYear(orders, sameMonthLastYear is null ? null : Orders(sameMonthLastYear, includeWholesale)),
            SkippedEurInvoiceCount = month.SkippedEurInvoiceCount,
            RevenueComputedAt = month.RevenueComputedAt,
            CostsComputedAt = month.CostsComputedAt,
            LastError = month.LastError,
        };
    }

    public MonthlyMarketingPerformanceDto Empty(YearMonth month, bool isPartial) => new()
    {
        Year = month.Year,
        Month = month.Month,
        MonthYearDisplay = Display(month),
        HasData = false,
        IsPartial = isPartial,
        ChannelCosts = _channels.Select(c => new ChannelCostDto { ChannelCode = c.Code, Label = c.Label }).ToList(),
    };

    public static decimal? Ratio(decimal numerator, decimal denominator) =>
        denominator == 0m ? null : numerator / denominator;

    public static decimal? PercentOfLastYear(decimal current, decimal? lastYear) =>
        lastYear is null || lastYear == 0m ? null : current / lastYear.Value * Percent;

    private static string Display(YearMonth ym) => $"{ym.Month:D2}/{ym.Year:D4}";

    private static int Orders(MarketingPerformanceMonth m, bool includeWholesale) =>
        m.RetailOrderCount + (includeWholesale ? m.WholesaleOrderCount : 0);

    private static decimal RevenueWithVat(MarketingPerformanceMonth m, bool includeWholesale) =>
        m.RetailRevenueWithVat + (includeWholesale ? m.WholesaleRevenueWithVat : 0m);

    private static decimal TotalCost(MarketingPerformanceMonth m) => m.ChannelCosts.Sum(c => c.CostWithoutVat);

    /// <summary>Configured order; a channel with no stored row shows as zero; stored rows for unconfigured codes are appended so nothing disappears silently.</summary>
    private List<ChannelCostDto> ChannelCosts(MarketingPerformanceMonth m)
    {
        var byCode = m.ChannelCosts.ToDictionary(c => c.ChannelCode, StringComparer.OrdinalIgnoreCase);
        var result = _channels.Select(ch =>
        {
            byCode.TryGetValue(ch.Code, out var stored);
            return new ChannelCostDto
            {
                ChannelCode = ch.Code,
                Label = ch.Label,
                CostWithoutVat = stored?.CostWithoutVat ?? 0m,
                InvoiceCount = stored?.InvoiceCount ?? 0,
            };
        }).ToList();

        var known = new HashSet<string>(_channels.Select(c => c.Code), StringComparer.OrdinalIgnoreCase);
        result.AddRange(m.ChannelCosts
            .Where(c => !known.Contains(c.ChannelCode))
            .Select(c => new ChannelCostDto { ChannelCode = c.ChannelCode, Label = c.ChannelCode, CostWithoutVat = c.CostWithoutVat, InvoiceCount = c.InvoiceCount }));
        return result;
    }
}
