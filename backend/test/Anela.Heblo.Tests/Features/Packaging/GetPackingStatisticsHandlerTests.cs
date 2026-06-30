using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackingStatistics;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Packaging;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class GetPackingStatisticsHandlerTests
{
    private static readonly DateTimeOffset PragueNow =
        new(2026, 6, 25, 14, 30, 0, TimeSpan.FromHours(2));

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _localNow;
        private readonly TimeZoneInfo _zone;

        public FakeTimeProvider(DateTimeOffset localNow)
        {
            _localNow = localNow;
            _zone = TimeZoneInfo.CreateCustomTimeZone("FakeZone", localNow.Offset, "FakeZone", "FakeZone");
        }

        public override DateTimeOffset GetUtcNow() => _localNow.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => _zone;
    }

    private static PackingStatistics EmptyStats() => new(
        TotalPackages: 0,
        TotalOrders: 0,
        PackagesWithTracking: 0,
        PackerAttributionSince: null,
        ThroughputDaily: new List<DailyThroughput>(),
        HourHeatmap: new List<HourBucket>(),
        ByPacker: new List<PackerThroughput>(),
        ByCarrier: new List<CarrierThroughput>(),
        PackagesPerOrder: new List<PackagesPerOrderBucket>());

    private static GetPackingStatisticsHandler MakeSut(out Mock<IPackageRepository> repo, PackingStatistics result)
    {
        repo = new Mock<IPackageRepository>();
        repo.Setup(r => r.GetPackingStatisticsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<TimeZoneInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return new GetPackingStatisticsHandler(repo.Object, new FakeTimeProvider(PragueNow));
    }

    [Fact]
    public async Task DefaultsToTrailing30DayWindowEndingToday()
    {
        var sut = MakeSut(out _, EmptyStats());

        var response = await sut.Handle(new GetPackingStatisticsRequest(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.ToDate.Date.Should().Be(new DateTime(2026, 6, 25));
        response.FromDate.Date.Should().Be(new DateTime(2026, 5, 27)); // 30-day inclusive window
    }

    [Fact]
    public async Task ReturnsInvalidDateRange_WhenFromAfterTo()
    {
        var sut = MakeSut(out _, EmptyStats());

        var response = await sut.Handle(
            new GetPackingStatisticsRequest { FromDate = new DateTime(2026, 6, 20), ToDate = new DateTime(2026, 6, 1) },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
    }

    [Fact]
    public async Task ComputesSummaryDerivations()
    {
        var stats = new PackingStatistics(
            TotalPackages: 10,
            TotalOrders: 4,
            PackagesWithTracking: 9,
            PackerAttributionSince: new DateOnly(2026, 6, 9),
            ThroughputDaily: new List<DailyThroughput>
            {
                new(new DateOnly(2026, 6, 10), 1, 3),
                new(new DateOnly(2026, 6, 11), 3, 7),
            },
            HourHeatmap: new List<HourBucket>
            {
                new(3, 9, 2),
                new(3, 10, 8),
            },
            ByPacker: new List<PackerThroughput>
            {
                new(Guid.NewGuid(), "Alice", 3, 7),
                new(Guid.NewGuid(), null, 1, 3),
            },
            ByCarrier: new List<CarrierThroughput> { new("DPD", "DPD", 10) },
            PackagesPerOrder: new List<PackagesPerOrderBucket> { new(1, 2), new(2, 2) });

        var sut = MakeSut(out _, stats);

        var response = await sut.Handle(new GetPackingStatisticsRequest(), CancellationToken.None);

        response.Summary.TotalPackages.Should().Be(10);
        response.Summary.TotalOrders.Should().Be(4);
        response.Summary.DistinctPackers.Should().Be(2);
        response.Summary.AveragePackagesPerOrder.Should().Be(2.5);
        response.Summary.TrackingCoveragePercent.Should().Be(90.0);
        response.Summary.BusiestDay!.PackageCount.Should().Be(7);
        response.Summary.BusiestHour!.Hour.Should().Be(10);
        response.PackerAttributionSince.Should().Be(new DateTime(2026, 6, 9));
        // Unattributed packer name falls back to a placeholder.
        response.ByPacker.Should().Contain(p => p.PackerName == "Neznámý");
    }

    [Fact]
    public async Task ZeroOrders_DoesNotDivideByZero()
    {
        var sut = MakeSut(out _, EmptyStats());

        var response = await sut.Handle(new GetPackingStatisticsRequest(), CancellationToken.None);

        response.Summary.AveragePackagesPerOrder.Should().Be(0);
        response.Summary.TrackingCoveragePercent.Should().Be(0);
        response.Summary.BusiestDay.Should().BeNull();
    }
}
