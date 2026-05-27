using System.Text.Json;
using Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs.DashboardTiles;

public class FailedJobsTileTests
{
    private readonly Mock<JobStorage> _storageMock = new();
    private readonly Mock<IMonitoringApi> _monitoringApiMock = new();
    private readonly FailedJobsTile _tile;

    public FailedJobsTileTests()
    {
        _storageMock.Setup(s => s.GetMonitoringApi()).Returns(_monitoringApiMock.Object);
        _tile = new FailedJobsTile(_storageMock.Object, NullLogger<FailedJobsTile>.Instance);
    }

    [Fact]
    public async Task LoadDataAsync_ZeroFailures_ReturnsSuccessWithCountZero()
    {
        _monitoringApiMock.Setup(a => a.FailedCount()).Returns(0L);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt64().Should().Be(0L);
        doc.RootElement.GetProperty("drillDown").GetProperty("url").GetString()
            .Should().Be("/hangfire/jobs/failed");
        doc.RootElement.GetProperty("drillDown").GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task LoadDataAsync_PositiveFailureCount_ReturnsSuccessWithCount()
    {
        _monitoringApiMock.Setup(a => a.FailedCount()).Returns(7L);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt64().Should().Be(7L);
    }

    [Fact]
    public async Task LoadDataAsync_MonitoringApiThrows_ReturnsErrorAndDoesNotPropagate()
    {
        _monitoringApiMock.Setup(a => a.FailedCount()).Throws(new InvalidOperationException("storage unavailable"));

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("error").GetString().Should().Be("storage unavailable");
        doc.RootElement.GetProperty("drillDown").GetProperty("url").GetString()
            .Should().Be("/hangfire/jobs/failed");
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
