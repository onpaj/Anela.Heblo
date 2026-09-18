using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class YearMonthTests
{
    [Fact]
    public void Start_And_EndExclusive_CoverTheWholeMonth()
    {
        var ym = new YearMonth(2026, 2);
        ym.Start.Should().Be(new DateTime(2026, 2, 1));
        ym.EndExclusive.Should().Be(new DateTime(2026, 3, 1));
        ym.LastDay.Should().Be(new DateTime(2026, 2, 28));
    }

    [Fact]
    public void AddMonths_WrapsYears()
    {
        new YearMonth(2026, 1).AddMonths(-1).Should().Be(new YearMonth(2025, 12));
        new YearMonth(2025, 12).AddMonths(1).Should().Be(new YearMonth(2026, 1));
    }

    [Fact]
    public void Parse_AcceptsYyyyDashMm_AndRejectsGarbage()
    {
        YearMonth.Parse("2026-09").Should().Be(new YearMonth(2026, 9));
        YearMonth.TryParse("2026-13", out _).Should().BeFalse();
        YearMonth.TryParse("garbage", out _).Should().BeFalse();
        YearMonth.TryParse(null, out _).Should().BeFalse();
    }

    [Fact]
    public void Range_IsInclusiveAndOrdered()
    {
        YearMonth.Range(new YearMonth(2025, 11), new YearMonth(2026, 2))
            .Should().Equal(new YearMonth(2025, 11), new YearMonth(2025, 12), new YearMonth(2026, 1), new YearMonth(2026, 2));
        YearMonth.MonthsBetween(new YearMonth(2025, 11), new YearMonth(2026, 2)).Should().Be(4);
    }

    [Fact]
    public void CompareTo_OrdersChronologically()
    {
        (new YearMonth(2025, 12) < new YearMonth(2026, 1)).Should().BeTrue();
        new YearMonth(2026, 3).ToString().Should().Be("2026-03");
    }
}
