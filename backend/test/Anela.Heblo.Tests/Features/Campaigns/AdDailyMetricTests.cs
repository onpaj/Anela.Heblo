using Anela.Heblo.Domain.Features.Campaigns;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Campaigns;

public class AdDailyMetricTests
{
    [Fact]
    public void Ctr_WhenImpressionsAreZero_ReturnsZero()
    {
        var metric = new AdDailyMetric { Impressions = 0, Clicks = 0 };

        metric.Ctr.Should().Be(0m);
    }

    [Fact]
    public void Ctr_WhenImpressionsArePositive_ReturnsClicksPercentage()
    {
        var metric = new AdDailyMetric { Impressions = 1000, Clicks = 50 };

        metric.Ctr.Should().Be(5m);
    }

    [Fact]
    public void Cpc_WhenClicksAreZero_ReturnsZero()
    {
        var metric = new AdDailyMetric { Clicks = 0, Spend = 100m };

        metric.Cpc.Should().Be(0m);
    }

    [Fact]
    public void Cpc_WhenClicksArePositive_ReturnsSpendPerClick()
    {
        var metric = new AdDailyMetric { Clicks = 10, Spend = 50m };

        metric.Cpc.Should().Be(5m);
    }

    [Fact]
    public void Roas_WhenSpendIsZero_ReturnsZero()
    {
        var metric = new AdDailyMetric { Spend = 0m, Revenue = 200m };

        metric.Roas.Should().Be(0m);
    }

    [Fact]
    public void Roas_WhenSpendIsPositive_ReturnsRevenueOverSpend()
    {
        var metric = new AdDailyMetric { Spend = 100m, Revenue = 400m };

        metric.Roas.Should().Be(4m);
    }
}
