using System.Text.Json;
using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.DataQuality.DashboardTiles;

public class DqtYesterdayStatusTileTests
{
    // Pinned "today" = 2026-05-06 10:00 local → yesterday = 2026-05-05
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 6, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Yesterday = new(2026, 5, 5);

    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly DqtYesterdayStatusTile _tile;

    public DqtYesterdayStatusTileTests()
    {
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(FixedNow);

        _tile = new DqtYesterdayStatusTile(
            _repositoryMock.Object,
            _timeProviderMock.Object,
            NullLogger<DqtYesterdayStatusTile>.Instance);
    }

    [Fact]
    public async Task LoadDataAsync_NoRunCoveringYesterday_ReturnsNoData()
    {
        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun?)null);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("no_data");
        doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("drillDown").GetProperty("href").GetString()
            .Should().Be("/automation/data-quality");
        doc.RootElement.GetProperty("drillDown").GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task LoadDataAsync_CompletedWithZeroMismatches_ReturnsSuccess()
    {
        var run = CreateRun(yesterday: Yesterday, status: DqtRunStatus.Completed, totalChecked: 123, totalMismatches: 0);

        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("runStatus").GetString().Should().Be("Completed");
        data.GetProperty("totalChecked").GetInt32().Should().Be(123);
        data.GetProperty("totalMismatches").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_CompletedWithMismatches_ReturnsWarning()
    {
        var run = CreateRun(yesterday: Yesterday, status: DqtRunStatus.Completed, totalChecked: 123, totalMismatches: 4);

        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("warning");
        doc.RootElement.GetProperty("data").GetProperty("totalMismatches").GetInt32().Should().Be(4);
        doc.RootElement.GetProperty("data").GetProperty("runStatus").GetString().Should().Be("Completed");
    }

    [Fact]
    public async Task LoadDataAsync_RunningRun_ReturnsWarningWithRunningStatus()
    {
        var run = CreateRun(yesterday: Yesterday, status: DqtRunStatus.Running, totalChecked: 0, totalMismatches: 0);

        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("warning");
        doc.RootElement.GetProperty("data").GetProperty("runStatus").GetString().Should().Be("Running");
    }

    [Fact]
    public async Task LoadDataAsync_FailedRun_ReturnsError()
    {
        var run = CreateRun(yesterday: Yesterday, status: DqtRunStatus.Failed, totalChecked: 0, totalMismatches: 0);

        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("data").GetProperty("runStatus").GetString().Should().Be("Failed");
    }

    [Fact]
    public async Task LoadDataAsync_RepositoryThrows_ReturnsErrorAndDoesNotPropagate()
    {
        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("drillDown").GetProperty("href").GetString()
            .Should().Be("/automation/data-quality");
    }

    [Fact]
    public void TileMetadata_MatchesSpec()
    {
        _tile.Title.Should().Be("DQT včera");
        _tile.Description.Should().Be("Stav včerejšího DQT testu faktur");
        _tile.Size.Should().Be(Anela.Heblo.Xcc.Services.Dashboard.TileSize.Medium);
        _tile.Category.Should().Be(Anela.Heblo.Xcc.Services.Dashboard.TileCategory.DataQuality);
        _tile.DefaultEnabled.Should().BeTrue();
        _tile.AutoShow.Should().BeFalse();
        _tile.RequiredPermissions.Should().BeEmpty();
    }

    private static DqtRun CreateRun(DateOnly yesterday, DqtRunStatus status, int totalChecked, int totalMismatches)
    {
        var run = DqtRun.Start(
            DqtTestType.IssuedInvoiceComparison,
            yesterday,
            yesterday,
            DqtTriggerType.Scheduled);

        if (status == DqtRunStatus.Completed)
        {
            run.Complete(totalChecked, totalMismatches);
        }
        else if (status == DqtRunStatus.Failed)
        {
            run.Fail("simulated failure");
        }
        // Running: leave as-is (DqtRun.Start sets Status = Running).
        return run;
    }

    private static JsonDocument ToJsonDoc(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload));
}
