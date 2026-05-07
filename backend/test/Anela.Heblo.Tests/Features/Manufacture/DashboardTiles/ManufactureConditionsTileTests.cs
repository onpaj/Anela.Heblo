using System.Text.Json;
using Anela.Heblo.Application.Features.Manufacture.DashboardTiles;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.Manufacture.DashboardTiles;

public class ManufactureConditionsTileTests
{
    private readonly Mock<IConditionsReadingProvider> _providerMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<ILogger<ManufactureConditionsTile>> _loggerMock;
    private readonly ManufactureConditionsTile _tile;
    private readonly DateTime _fixedNow = new(2026, 5, 7, 10, 0, 0, DateTimeKind.Utc);

    public ManufactureConditionsTileTests()
    {
        _providerMock = new Mock<IConditionsReadingProvider>();
        _timeProviderMock = new Mock<TimeProvider>();
        _loggerMock = new Mock<ILogger<ManufactureConditionsTile>>();
        _tile = new ManufactureConditionsTile(
            _providerMock.Object,
            _timeProviderMock.Object,
            _loggerMock.Object);

        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(_fixedNow));
    }

    [Fact]
    public void Metadata_HasCorrectValues()
    {
        _tile.Title.Should().Be("Podmínky ve výrobně");
        _tile.Description.Should().Be("Aktuální teplota a vlhkost (vnitřní / venkovní)");
        _tile.Size.Should().Be(TileSize.Medium);
        _tile.Category.Should().Be(TileCategory.Manufacture);
        _tile.DefaultEnabled.Should().BeTrue();
        _tile.AutoShow.Should().BeTrue();
        _tile.ComponentType.Should().Be(typeof(object));
        _tile.RequiredPermissions.Should().BeEmpty();
    }

    [Fact]
    public void TileId_IsManufactureConditions()
    {
        TileExtensions.GetTileId<ManufactureConditionsTile>().Should().Be("manufactureconditions");
    }

    [Fact]
    public async Task LoadDataAsync_WithLiveSnapshot_ReturnsAllFourReadingsAndLiveSource()
    {
        // Arrange
        var snapshot = new ConditionsSnapshot(
            InnerTemperature: 21.5m,
            InnerHumidity: 55m,
            OuterTemperature: 14.2m,
            OuterHumidity: 72m,
            RecordedAt: _fixedNow,
            Source: ConditionsReadingSource.Live);

        _providerMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("status").GetString().Should().Be("success");
        var data = json.GetProperty("data");
        data.GetProperty("innerTemperature").GetDecimal().Should().Be(21.5m);
        data.GetProperty("innerHumidity").GetDecimal().Should().Be(55m);
        data.GetProperty("outerTemperature").GetDecimal().Should().Be(14.2m);
        data.GetProperty("outerHumidity").GetDecimal().Should().Be(72m);
        data.GetProperty("source").GetString().Should().Be("Live");
    }

    [Fact]
    public async Task LoadDataAsync_WithUnavailableSnapshot_ReturnsNullReadingsAndUnavailableSource()
    {
        // Arrange
        var snapshot = new ConditionsSnapshot(
            InnerTemperature: null,
            InnerHumidity: null,
            OuterTemperature: null,
            OuterHumidity: null,
            RecordedAt: _fixedNow,
            Source: ConditionsReadingSource.Unavailable);

        _providerMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        var data = json.GetProperty("data");
        data.GetProperty("innerTemperature").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("innerHumidity").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("outerTemperature").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("outerHumidity").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("source").GetString().Should().Be("Unavailable");
    }

    [Fact]
    public async Task LoadDataAsync_WithPartialSnapshot_ReturnsPartialSource()
    {
        // Arrange
        var snapshot = new ConditionsSnapshot(
            InnerTemperature: 20.0m,
            InnerHumidity: null,
            OuterTemperature: null,
            OuterHumidity: null,
            RecordedAt: _fixedNow,
            Source: ConditionsReadingSource.Partial);

        _providerMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("data").GetProperty("source").GetString().Should().Be("Partial");
        json.GetProperty("data").GetProperty("innerTemperature").GetDecimal().Should().Be(20.0m);
    }

    [Fact]
    public async Task LoadDataAsync_WhenProviderThrows_ReturnsFallbackWithUnavailableSource()
    {
        // Arrange
        _providerMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("HA unreachable"));

        // Act — must not throw
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("status").GetString().Should().Be("success");
        var data = json.GetProperty("data");
        data.GetProperty("source").GetString().Should().Be("Unavailable");
        data.GetProperty("innerTemperature").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task LoadDataAsync_WhenCancelled_PropagatesCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _providerMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _tile.LoadDataAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task LoadDataAsync_MetadataSource_IsHomeAssistant()
    {
        // Arrange
        _providerMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConditionsSnapshot(null, null, null, null, _fixedNow, ConditionsReadingSource.Unavailable));

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("metadata").GetProperty("source").GetString().Should().Be("HomeAssistant");
    }
}
