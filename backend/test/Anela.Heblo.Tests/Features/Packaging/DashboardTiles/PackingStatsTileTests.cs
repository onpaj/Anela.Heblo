using System.Net.Http;
using System.Text.Json;
using Anela.Heblo.Application.Features.Packaging.Contracts;
using Anela.Heblo.Application.Features.Packaging.DashboardTiles;
using Anela.Heblo.Domain.Features.Packaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging.DashboardTiles;

public sealed class PackingStatsTileTests
{
    private static readonly DateTimeOffset PragueNow =
        new(2026, 6, 10, 14, 30, 0, TimeSpan.FromHours(2));

    // TimeProvider.GetLocalNow() is not virtual, so we subclass instead of using Moq.
    // Overriding GetUtcNow() + LocalTimeZone makes GetLocalNow() return PragueNow correctly.
    // Mirrors the FakeTimeProvider used by GetPackingDashboardHandlerTests in the sibling folder.
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

    private readonly Mock<IPackageRepository> _repo = new();
    private readonly Mock<IPackingOrderCountSource> _packingClient = new();

    private PackingStatsTile MakeSut() =>
        new(
            _repo.Object,
            _packingClient.Object,
            new FakeTimeProvider(PragueNow),
            NullLogger<PackingStatsTile>.Instance);

    private static JsonDocument ToJsonDoc(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload));

    // FR-1 (happy path) + FR-3 (null packer name -> "Neznámý"), folded per arch-review Decision 3.
    [Fact]
    public async Task LoadDataAsync_AllDependenciesSucceed_ReturnsSuccessWithCountsAndPackers()
    {
        // Arrange
        var packerId = Guid.NewGuid();
        var byPacker = new List<PackerPackingSummary>
        {
            new(packerId, "Alice", 4),
            new(null, null, 1), // null name -> "Neznámý", null id -> JSON null
        };
        _repo
            .Setup(r => r.GetPackedTodayByPackerAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((6, byPacker));
        _packingClient
            .Setup(c => c.GetOrdersBeingPackedCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        _packingClient
            .Setup(c => c.GetOrdersBeingProcessedCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(9);

        var sut = MakeSut();

        // Act
        var result = await sut.LoadDataAsync();

        // Assert
        var doc = ToJsonDoc(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");

        var data = root.GetProperty("data");
        data.GetProperty("ordersBeingPackedCount").GetInt32().Should().Be(4);
        data.GetProperty("ordersBeingProcessedCount").GetInt32().Should().Be(9);
        data.GetProperty("ordersBeingPackedCountLastSync").ValueKind.Should().Be(JsonValueKind.String);
        data.GetProperty("totalOrdersPackedToday").GetInt32().Should().Be(6);

        var packers = data.GetProperty("packedByPacker");
        packers.GetArrayLength().Should().Be(2);

        packers[0].GetProperty("packerId").GetString().Should().Be(packerId.ToString());
        packers[0].GetProperty("packerName").GetString().Should().Be("Alice");
        packers[0].GetProperty("orderCount").GetInt32().Should().Be(4);

        packers[1].GetProperty("packerId").ValueKind.Should().Be(JsonValueKind.Null);
        packers[1].GetProperty("packerName").GetString().Should().Be("Neznámý"); // FR-3
        packers[1].GetProperty("orderCount").GetInt32().Should().Be(1);
    }

    // FR-2: Shoptet failure is isolated — tile stays "success", only Shoptet-derived fields null.
    [Fact]
    public async Task LoadDataAsync_ShoptetClientThrows_ReturnsSuccessWithNullCountsAndPackersPopulated()
    {
        // Arrange
        var byPacker = new List<PackerPackingSummary> { new(Guid.NewGuid(), "Alice", 3) };
        _repo
            .Setup(r => r.GetPackedTodayByPackerAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((5, byPacker));
        _packingClient
            .Setup(c => c.GetOrdersBeingPackedCountAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet unreachable"));
        _packingClient
            .Setup(c => c.GetOrdersBeingProcessedCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(9);

        var sut = MakeSut();

        // Act — must not throw (inner catch swallows the Shoptet exception)
        var result = await sut.LoadDataAsync();

        // Assert
        var doc = ToJsonDoc(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");

        var data = root.GetProperty("data");
        data.GetProperty("ordersBeingPackedCount").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("ordersBeingProcessedCount").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("ordersBeingPackedCountLastSync").ValueKind.Should().Be(JsonValueKind.Null);

        // Packer data is untouched — proves failure isolation.
        data.GetProperty("totalOrdersPackedToday").GetInt32().Should().Be(5);
        var packers = data.GetProperty("packedByPacker");
        packers.GetArrayLength().Should().Be(1);
        packers[0].GetProperty("packerName").GetString().Should().Be("Alice");
        packers[0].GetProperty("orderCount").GetInt32().Should().Be(3);
    }

    // FR-4: Repository failure -> outer catch -> "error" shape with no data property.
    [Fact]
    public async Task LoadDataAsync_RepositoryThrows_ReturnsError()
    {
        // Arrange
        _repo
            .Setup(r => r.GetPackedTodayByPackerAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var sut = MakeSut();

        // Act — must not throw (outer catch handles it)
        var result = await sut.LoadDataAsync();

        // Assert
        var doc = ToJsonDoc(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("error");
        root.GetProperty("error").GetString().Should().Be("Nepodařilo se načíst data balení");
        // The error shape OMITS data entirely (differs from FailedJobsTile's data = null).
        root.TryGetProperty("data", out _).Should().BeFalse();
    }
}
