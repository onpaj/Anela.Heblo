using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackingDashboard;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Packaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class GetPackingDashboardHandlerTests
{
    private static readonly DateTimeOffset PragueNow =
        new(2026, 6, 10, 14, 30, 0, TimeSpan.FromHours(2));

    // TimeProvider.GetLocalNow() is not virtual, so we subclass instead of using Moq.
    // Overriding GetUtcNow() + LocalTimeZone makes GetLocalNow() return PragueNow correctly.
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _localNow;
        private readonly TimeZoneInfo _zone;

        public FakeTimeProvider(DateTimeOffset localNow)
        {
            _localNow = localNow;
            _zone = TimeZoneInfo.CreateCustomTimeZone(
                "FakeZone", localNow.Offset, "FakeZone", "FakeZone");
        }

        public override DateTimeOffset GetUtcNow() => _localNow.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => _zone;
    }

    private static GetPackingDashboardHandler MakeSut(
        out Mock<IPackageRepository> repo,
        out Mock<IPackingOrderClient> packingClient,
        (int TotalDistinctOrders, IReadOnlyList<PackerPackingSummary> ByPacker) repoResult,
        int shoptetCount = 5)
    {
        repo = new Mock<IPackageRepository>();
        repo.Setup(r => r.GetPackedTodayByPackerAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(repoResult);

        packingClient = new Mock<IPackingOrderClient>();
        packingClient.Setup(c => c.GetOrdersBeingPackedCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(shoptetCount);

        return new GetPackingDashboardHandler(
            repo.Object,
            packingClient.Object,
            new FakeTimeProvider(PragueNow),
            NullLogger<GetPackingDashboardHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ReturnsCorrectTotalAndPackerBreakdown()
    {
        // Arrange
        var packerId = Guid.NewGuid();
        var byPacker = new List<PackerPackingSummary>
        {
            new(packerId, "Alice", 4),
            new(null, "Bob", 2),
        };
        var sut = MakeSut(out _, out _, (6, byPacker), shoptetCount: 3);

        // Act
        var result = await sut.Handle(new GetPackingDashboardRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalOrdersPackedToday.Should().Be(6);
        result.OrdersBeingPackedCount.Should().Be(3);
        result.PackedByPacker.Should().HaveCount(2);
        result.PackedByPacker[0].PackerId.Should().Be(packerId);
        result.PackedByPacker[0].PackerName.Should().Be("Alice");
        result.PackedByPacker[0].OrderCount.Should().Be(4);
        result.PackedByPacker[1].PackerName.Should().Be("Bob");
        result.PackedByPacker[1].OrderCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_PassesTodayWindowToRepository_InPragueTime()
    {
        // Arrange — today in Prague is 2026-06-10, offset +02:00
        var sut = MakeSut(out var repo, out _, (0, Array.Empty<PackerPackingSummary>()), shoptetCount: 0);
        var expectedStart = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.FromHours(2));
        var expectedEnd = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.FromHours(2));

        // Act
        await sut.Handle(new GetPackingDashboardRequest(), CancellationToken.None);

        // Assert
        repo.Verify(r => r.GetPackedTodayByPackerAsync(expectedStart, expectedEnd, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SetsOrdersBeingPackedCountToNull_WhenShoptetThrows()
    {
        // Arrange
        var sut = MakeSut(out _, out var packingClient, (2, Array.Empty<PackerPackingSummary>()));
        packingClient.Setup(c => c.GetOrdersBeingPackedCountAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet unreachable"));

        // Act
        var result = await sut.Handle(new GetPackingDashboardRequest(), CancellationToken.None);

        // Assert — dashboard still succeeds, count is null
        result.Success.Should().BeTrue();
        result.OrdersBeingPackedCount.Should().BeNull();
        result.TotalOrdersPackedToday.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ShowsUnknownPackerName_WhenPackedByIsNull()
    {
        // Arrange
        var byPacker = new List<PackerPackingSummary> { new(null, null, 1) };
        var sut = MakeSut(out _, out _, (1, byPacker));

        // Act
        var result = await sut.Handle(new GetPackingDashboardRequest(), CancellationToken.None);

        // Assert
        result.PackedByPacker[0].PackerName.Should().Be("Neznámý");
    }
}
