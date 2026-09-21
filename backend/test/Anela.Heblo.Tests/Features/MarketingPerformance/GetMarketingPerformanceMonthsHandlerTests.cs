using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceMonths;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class GetMarketingPerformanceMonthsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 10, 0, 0, TimeSpan.FromHours(2));
    private readonly Mock<IMarketingPerformanceRepository> _repo = new();

    private static IOptions<MarketingPerformanceOptions> Options() => Microsoft.Extensions.Options.Options.Create(new MarketingPerformanceOptions
    {
        VatRate = 1.21m,
        Channels = { new MarketingChannelOptions { Code = "meta", Label = "FB/IG", VatIds = { "IE1" } } },
    });

    private GetMarketingPerformanceMonthsHandler Handler() =>
        new(_repo.Object, Options(), new FakeTimeProvider(Now), NullLogger<GetMarketingPerformanceMonthsHandler>.Instance);

    private static MarketingPerformanceMonth Row(int y, int m, int orders, decimal revenue, decimal cost) => new()
    {
        Year = y, Month = m, RetailOrderCount = orders, RetailRevenueWithVat = revenue,
        WholesaleOrderCount = 5, WholesaleRevenueWithVat = 1210m,
        ChannelCosts = { new MarketingPerformanceChannelCost { ChannelCode = "meta", CostWithoutVat = cost, InvoiceCount = 1 } },
    };

    [Fact]
    public async Task Handle_FillsMissingMonthsAndComputesYoyFromPriorYear()
    {
        _repo.Setup(r => r.GetRangeAsync(new YearMonth(2025, 7), new YearMonth(2026, 8), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<MarketingPerformanceMonth> { Row(2025, 7, 100, 121000m, 1000m), Row(2026, 7, 150, 242000m, 1500m) });
        _repo.Setup(r => r.GetLastComputedAtAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DateTime(2026, 9, 18, 3, 0, 0));

        var response = await Handler().Handle(new GetMarketingPerformanceMonthsRequest { From = "2026-07", To = "2026-08" }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Months.Select(m => (m.Year, m.Month)).Should().Equal((2026, 7), (2026, 8));
        response.Months[0].HasData.Should().BeTrue();
        response.Months[0].YoyOrdersPercent.Should().Be(150m);
        response.Months[0].YoyRevenuePercent.Should().Be(200m);
        response.Months[0].YoyCostPercent.Should().Be(150m);
        response.Months[1].HasData.Should().BeFalse();
        response.Months[1].IsPartial.Should().BeFalse();
        response.Channels.Should().ContainSingle(c => c.Code == "meta" && c.Label == "FB/IG");
        response.LastRefreshAt.Should().Be(new DateTime(2026, 9, 18, 3, 0, 0));
        response.VatRate.Should().Be(1.21m);
    }

    [Fact]
    public async Task Handle_DefaultRange_IsLast36MonthsEndingNow_CurrentMonthPartial()
    {
        _repo.Setup(r => r.GetRangeAsync(new YearMonth(2022, 10), new YearMonth(2026, 9), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<MarketingPerformanceMonth>());

        var response = await Handler().Handle(new GetMarketingPerformanceMonthsRequest(), CancellationToken.None);

        response.From.Should().Be("2023-10");
        response.To.Should().Be("2026-09");
        response.Months.Should().HaveCount(36);
        response.Months.Last().IsPartial.Should().BeTrue();
        response.Months.First().IsPartial.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_IncludeWholesale_AddsWholesale()
    {
        _repo.Setup(r => r.GetRangeAsync(It.IsAny<YearMonth>(), It.IsAny<YearMonth>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<MarketingPerformanceMonth> { Row(2026, 7, 150, 242000m, 1500m) });

        var response = await Handler().Handle(new GetMarketingPerformanceMonthsRequest { From = "2026-07", To = "2026-07", IncludeWholesale = true }, CancellationToken.None);

        response.Months[0].Orders.Should().Be(155);
        response.Months[0].RevenueWithVat.Should().Be(243210m);
        response.IncludeWholesale.Should().BeTrue();
    }

    [Theory]
    [InlineData("2026-13", "2026-09", ErrorCodes.MarketingPerformanceInvalidMonthRange)]
    [InlineData("2026-09", "2026-01", ErrorCodes.MarketingPerformanceInvalidMonthRange)]
    [InlineData("2020-01", "2026-09", ErrorCodes.MarketingPerformanceRangeTooLarge)]
    public async Task Handle_BadRange_ReturnsErrorCode(string from, string to, ErrorCodes expected)
    {
        var response = await Handler().Handle(new GetMarketingPerformanceMonthsRequest { From = from, To = to }, CancellationToken.None);
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(expected);
    }
}
