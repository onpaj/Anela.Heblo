using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class MarketingMetricsCalculatorTests
{
    private static readonly IReadOnlyList<MarketingChannelDefinition> Channels = new[]
    {
        new MarketingChannelDefinition { Code = "meta", Label = "FB/IG", VatIds = new[] { "IE1" } },
        new MarketingChannelDefinition { Code = "google", Label = "Google", VatIds = new[] { "IE2" } },
        new MarketingChannelDefinition { Code = "sklik", Label = "S-Klik", VatIds = new[] { "CZ1" } },
    };

    private static readonly MarketingMetricsCalculator Calc = new(1.21m, Channels);

    // Spreadsheet row 2026-01: FB/IG 386500, Google 124126, S-Klik 14456, Shoptet s DPH 2 586 556, 2354 orders.
    private static MarketingPerformanceMonth Jan2026() => new()
    {
        Year = 2026, Month = 1,
        RetailOrderCount = 2354, RetailRevenueWithVat = 2_586_556m,
        WholesaleOrderCount = 33, WholesaleRevenueWithVat = 285_591m,
        SkippedEurInvoiceCount = 2, IsLocked = true,
        RevenueComputedAt = new DateTime(2026, 9, 1), CostsComputedAt = new DateTime(2026, 9, 1),
        ChannelCosts =
        {
            new MarketingPerformanceChannelCost { ChannelCode = "meta", CostWithoutVat = 386_500m, InvoiceCount = 2 },
            new MarketingPerformanceChannelCost { ChannelCode = "google", CostWithoutVat = 124_126m, InvoiceCount = 1 },
            new MarketingPerformanceChannelCost { ChannelCode = "sklik", CostWithoutVat = 14_456m, InvoiceCount = 1 },
        },
    };

    private static MarketingPerformanceMonth Jan2025() => new()
    {
        Year = 2025, Month = 1, RetailOrderCount = 3021, RetailRevenueWithVat = 2_802_542m,
        ChannelCosts = { new MarketingPerformanceChannelCost { ChannelCode = "meta", CostWithoutVat = 583_917m, InvoiceCount = 3 } },
    };

    [Fact]
    public void Build_RetailOnly_MatchesSpreadsheetFormulas()
    {
        var dto = Calc.Build(Jan2026(), Jan2025(), includeWholesale: false, isPartial: false);

        dto.Year.Should().Be(2026);
        dto.Month.Should().Be(1);
        dto.MonthYearDisplay.Should().Be("01/2026");
        dto.Orders.Should().Be(2354);
        dto.RevenueWithVat.Should().Be(2_586_556m);
        dto.RevenueWithoutVat.Should().BeApproximately(2_137_649.59m, 0.01m);
        dto.TotalCost.Should().Be(525_082m);
        dto.Pno.Should().BeApproximately(24.56m, 0.01m);            // 525082 / 2137649.59 * 100
        dto.Roas.Should().BeApproximately(407.11m, 0.01m);          // inverse
        dto.Profit.Should().BeApproximately(1_612_567.59m, 0.01m);
        dto.AvgOrderValue.Should().BeApproximately(908.09m, 0.01m);
        dto.CostPerOrder.Should().BeApproximately(223.06m, 0.01m);
        dto.YoyCostPercent.Should().BeApproximately(89.92m, 0.01m);     // 525082 / 583917
        dto.YoyRevenuePercent.Should().BeApproximately(92.29m, 0.01m);  // 2586556 / 2802542
        dto.YoyOrdersPercent.Should().BeApproximately(77.92m, 0.01m);   // 2354 / 3021
        dto.HasData.Should().BeTrue();
        dto.IsLocked.Should().BeTrue();
        dto.SkippedEurInvoiceCount.Should().Be(2);
    }

    [Fact]
    public void Build_IncludeWholesale_AddsWholesaleToOrdersAndRevenue()
    {
        var dto = Calc.Build(Jan2026(), null, includeWholesale: true, isPartial: false);

        dto.Orders.Should().Be(2387);
        dto.RevenueWithVat.Should().Be(2_872_147m);
        dto.YoyRevenuePercent.Should().BeNull("no prior year supplied");
    }

    [Fact]
    public void Build_ChannelCosts_FollowConfiguredOrderAndFillMissingWithZero()
    {
        var month = Jan2026();
        month.ChannelCosts.RemoveAll(c => c.ChannelCode == "google");

        var dto = Calc.Build(month, null, false, false);

        dto.ChannelCosts.Select(c => c.ChannelCode).Should().Equal("meta", "google", "sklik");
        dto.ChannelCosts.Single(c => c.ChannelCode == "google").CostWithoutVat.Should().Be(0m);
        dto.ChannelCosts.Single(c => c.ChannelCode == "meta").Label.Should().Be("FB/IG");
        dto.TotalCost.Should().Be(400_956m);
    }

    [Fact]
    public void Build_ChannelCostsDifferingOnlyByCase_AreSummedIntoOneRow()
    {
        var month = new MarketingPerformanceMonth
        {
            Year = 2026, Month = 9,
            ChannelCosts =
            {
                new MarketingPerformanceChannelCost { ChannelCode = "meta", CostWithoutVat = 100m, InvoiceCount = 1 },
                new MarketingPerformanceChannelCost { ChannelCode = "META", CostWithoutVat = 50m, InvoiceCount = 2 },
            },
        };

        var dto = Calc.Build(month, null, false, false);

        dto.ChannelCosts.Should().HaveCount(3);
        var meta = dto.ChannelCosts.Single(c => c.ChannelCode == "meta");
        meta.CostWithoutVat.Should().Be(150m);
        meta.InvoiceCount.Should().Be(3);
        dto.TotalCost.Should().Be(150m);
    }

    [Fact]
    public void Build_ZeroRevenueOrOrders_YieldsNullRatiosNotExceptions()
    {
        var month = new MarketingPerformanceMonth { Year = 2026, Month = 9, ChannelCosts = { new() { ChannelCode = "meta", CostWithoutVat = 100m } } };

        var dto = Calc.Build(month, null, false, isPartial: true);

        dto.Pno.Should().BeNull();
        dto.Roas.Should().Be(0m); // spend with no revenue is a real 0% return, not an undefined one
        dto.AvgOrderValue.Should().BeNull();
        dto.CostPerOrder.Should().BeNull();
        dto.Profit.Should().Be(-100m);
        dto.IsPartial.Should().BeTrue();
    }

    [Fact]
    public void Build_ZeroCost_RoasIsNullPnoIsZero()
    {
        var month = new MarketingPerformanceMonth { Year = 2026, Month = 9, RetailOrderCount = 10, RetailRevenueWithVat = 1210m };
        var dto = Calc.Build(month, null, false, false);
        dto.Pno.Should().Be(0m);
        dto.Roas.Should().BeNull();
    }

    [Fact]
    public void Build_LastYearZero_YoyIsNull()
    {
        var lastYear = new MarketingPerformanceMonth { Year = 2025, Month = 1 };
        var dto = Calc.Build(Jan2026(), lastYear, false, false);
        dto.YoyCostPercent.Should().BeNull();
        dto.YoyOrdersPercent.Should().BeNull();
    }

    [Fact]
    public void Empty_HasNoDataAndZeroSums()
    {
        var dto = Calc.Empty(new YearMonth(2026, 10), isPartial: false);
        dto.HasData.Should().BeFalse();
        dto.Orders.Should().Be(0);
        dto.ChannelCosts.Should().HaveCount(3).And.OnlyContain(c => c.CostWithoutVat == 0m);
        dto.Pno.Should().BeNull();
        dto.RevenueComputedAt.Should().BeNull();
    }
}
