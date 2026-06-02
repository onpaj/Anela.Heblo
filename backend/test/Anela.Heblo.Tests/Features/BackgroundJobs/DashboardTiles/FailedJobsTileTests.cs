using System.Text.Json;
using Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs.DashboardTiles;

public sealed class FailedJobsTileTests
{
    private readonly Mock<IFailedJobCounter> _counterMock = new();
    private readonly FailedJobsTile _tile;

    public FailedJobsTileTests()
    {
        _tile = new FailedJobsTile(_counterMock.Object, NullLogger<FailedJobsTile>.Instance);
    }

    [Fact]
    public async Task LoadDataAsync_ZeroFailures_ReturnsSuccessWithCountZero()
    {
        _counterMock
            .Setup(c => c.GetFailedCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt64().Should().Be(0L);
        var drillDown = doc.RootElement.GetProperty("drillDown");
        drillDown.GetProperty("routeKey").GetString().Should().Be("hangfireFailedJobs");
        drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
        drillDown.GetProperty("tooltip").GetString().Should().Be("Open Hangfire failed jobs");
        drillDown.TryGetProperty("href", out _).Should().BeFalse();
        drillDown.TryGetProperty("url", out _).Should().BeFalse();
    }

    [Fact]
    public async Task LoadDataAsync_PositiveFailureCount_ReturnsSuccessWithCount()
    {
        _counterMock
            .Setup(c => c.GetFailedCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(7L);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt64().Should().Be(7L);
    }

    [Fact]
    public async Task LoadDataAsync_CounterThrows_ReturnsErrorAndDoesNotPropagate()
    {
        _counterMock
            .Setup(c => c.GetFailedCountAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("error").GetString().Should().Be("Failed to retrieve job count. See server logs.");
        var drillDown = doc.RootElement.GetProperty("drillDown");
        drillDown.GetProperty("routeKey").GetString().Should().Be("hangfireFailedJobs");
        drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
        drillDown.GetProperty("tooltip").GetString().Should().Be("Open Hangfire failed jobs");
        drillDown.TryGetProperty("href", out _).Should().BeFalse();
        drillDown.TryGetProperty("url", out _).Should().BeFalse();
    }

    [Fact]
    public void TileMetadata_MatchesSpec()
    {
        _tile.Title.Should().Be("Failed background jobs");
        _tile.Description.Should().Be("Hangfire jobs in the failed queue");
        _tile.Size.Should().Be(TileSize.Small);
        _tile.Category.Should().Be(TileCategory.System);
        _tile.DefaultEnabled.Should().BeTrue();
        _tile.AutoShow.Should().BeFalse();
        _tile.RequiredPermissions.Should().BeEmpty();
    }

    private static JsonDocument ToJsonDoc(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload));
}
