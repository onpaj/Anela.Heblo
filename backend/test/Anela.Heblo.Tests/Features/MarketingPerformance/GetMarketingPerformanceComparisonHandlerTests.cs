using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceComparison;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class GetMarketingPerformanceComparisonHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 10, 0, 0, TimeSpan.FromHours(2));
    private readonly Mock<IMarketingPerformanceRepository> _repo = new();

    private GetMarketingPerformanceComparisonHandler Handler() => new(
        _repo.Object,
        Microsoft.Extensions.Options.Options.Create(new MarketingPerformanceOptions
        {
            Channels = { new MarketingChannelOptions { Code = "meta", Label = "FB/IG", VatIds = { "IE1" } } },
        }),
        new FakeTimeProvider(Now), NullLogger<GetMarketingPerformanceComparisonHandler>.Instance);

    private static MarketingPerformanceMonth Row(int y, int m, int orders, decimal revenue, decimal cost) => new()
    {
        Year = y, Month = m, RetailOrderCount = orders, RetailRevenueWithVat = revenue,
        ChannelCosts = { new MarketingPerformanceChannelCost { ChannelCode = "meta", CostWithoutVat = cost } },
    };

    [Fact]
    public async Task Handle_ThreeYears_OneSeriesPerYearNewestFirst_TwelveCellsEach_WithYtd()
    {
        _repo.Setup(r => r.GetRangeAsync(new YearMonth(2023, 1), new YearMonth(2026, 12), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<MarketingPerformanceMonth>
             {
                 Row(2024, 6, 10, 1210m, 100m), Row(2025, 6, 20, 2420m, 200m), Row(2026, 6, 30, 3630m, 300m), Row(2026, 9, 5, 605m, 50m),
             });

        var response = await Handler().Handle(new GetMarketingPerformanceComparisonRequest { Years = 3 }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.AnchorYear.Should().Be(2026);
        response.CurrentMonth.Should().Be(9);
        response.Series.Select(s => s.Year).Should().Equal(2026, 2025, 2024);
        response.Series.Should().OnlyContain(s => s.Months.Count == 12);
        var s2026 = response.Series[0];
        s2026.Months[5].HasData.Should().BeTrue();
        s2026.Months[5].YoyOrdersPercent.Should().Be(150m);      // 30 vs 20 in June 2025
        s2026.Months[8].IsPartial.Should().BeTrue();             // September 2026
        s2026.Months[9].HasData.Should().BeFalse();              // October 2026 not yet
        s2026.YtdOrders.Should().Be(35);
        s2026.YtdRevenueWithoutVat.Should().Be(3500m);
        s2026.YtdTotalCost.Should().Be(350m);
        s2026.YtdPno.Should().Be(10m);
        response.Series[2].Months[5].YoyOrdersPercent.Should().BeNull("2023 was not loaded for the oldest series' YoY");
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(5, 3)]
    public async Task Handle_ClampsYearsTo2To3(int requested, int expected)
    {
        _repo.Setup(r => r.GetRangeAsync(It.IsAny<YearMonth>(), It.IsAny<YearMonth>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<MarketingPerformanceMonth>());
        var response = await Handler().Handle(new GetMarketingPerformanceComparisonRequest { Years = requested }, CancellationToken.None);
        response.Series.Should().HaveCount(expected);
    }
}
