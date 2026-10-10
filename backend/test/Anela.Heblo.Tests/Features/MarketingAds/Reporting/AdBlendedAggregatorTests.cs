using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Reporting;

public class AdBlendedAggregatorTests
{
    private static AdBlendedDayRow Day(int month, int day, decimal cost, decimal revenue, bool nonCzk = false) => new()
    {
        Date = new DateOnly(2026, month, day), AdCostCzk = cost, GoogleAdsCostCzk = cost, Orders = 1,
        RevenueWithoutVatCzk = revenue, EshopRevenueWithoutVatCzk = revenue, HasNonCzkAdCost = nonCzk,
    };

    [Fact]
    public void Monthly_sums_days_and_recomputes_ratios_from_sums()
    {
        // Arrange — ratios must come from summed cost and revenue, never by averaging daily ratios
        var rows = new[] { Day(9, 1, 100m, 1000m), Day(9, 2, 50m, 2000m, nonCzk: true), Day(10, 1, 10m, 0m) };

        // Act
        var periods = AdBlendedAggregator.ToPeriods(rows, BlendedGranularity.Monthly, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 15));

        // Assert
        periods.Should().HaveCount(2);
        var september = periods[0];
        september.PeriodStart.Should().Be(new DateOnly(2026, 9, 1));
        september.AdCostCzk.Should().Be(150m);
        september.RevenueWithoutVatCzk.Should().Be(3000m);
        september.BlendedRoas.Should().Be(20.0000m);
        september.PnoPercent.Should().Be(5.00m);
        september.HasNonCzkAdCost.Should().BeTrue();
        september.IsPartial.Should().BeFalse();
        periods[1].IsPartial.Should().BeTrue(); // October cut at the 15th
        periods[1].PnoPercent.Should().BeNull(); // no revenue
    }

    [Fact]
    public void Daily_keeps_one_period_per_row()
    {
        var periods = AdBlendedAggregator.ToPeriods(new[] { Day(9, 1, 100m, 1000m) }, BlendedGranularity.Daily, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1));

        periods.Should().ContainSingle().Which.BlendedRoas.Should().Be(10.0000m);
    }

    [Fact]
    public void Total_spans_all_rows()
    {
        var total = AdBlendedAggregator.Total(new[] { Day(9, 1, 100m, 1000m), Day(10, 1, 100m, 1000m) }, new DateOnly(2026, 9, 1));

        total.AdCostCzk.Should().Be(200m);
        total.Orders.Should().Be(2);
        total.BlendedRoas.Should().Be(10.0000m);
    }
}
