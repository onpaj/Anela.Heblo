using System.Text.Json;
using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.DataQuality.DashboardTiles;

public class DataQualityStatusTileTests
{
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly DataQualityStatusTile _tile;

    public DataQualityStatusTileTests()
    {
        _tile = new DataQualityStatusTile(_repositoryMock.Object);
    }

    [Fact]
    public async Task LoadDataAsync_NoRun_ReturnsNoDataWithRouteKey()
    {
        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAsync(
                DqtTestType.IssuedInvoiceComparison, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun?)null);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("no_data");
        doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);

        var drillDown = doc.RootElement.GetProperty("drillDown");
        drillDown.GetProperty("routeKey").GetString().Should().Be("dataQuality");
        drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
        drillDown.TryGetProperty("href", out _).Should().BeFalse();
        drillDown.TryGetProperty("url", out _).Should().BeFalse();
    }

    [Fact]
    public async Task LoadDataAsync_RunWithoutMismatches_ReturnsSuccessWithRouteKey()
    {
        var run = CreateCompletedRun(totalChecked: 50, totalMismatches: 0);

        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAsync(
                DqtTestType.IssuedInvoiceComparison, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("drillDown").GetProperty("routeKey").GetString().Should().Be("dataQuality");
    }

    [Fact]
    public async Task LoadDataAsync_RepositoryThrows_ReturnsErrorWithRouteKey()
    {
        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAsync(
                DqtTestType.IssuedInvoiceComparison, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        var drillDown = doc.RootElement.GetProperty("drillDown");
        drillDown.GetProperty("routeKey").GetString().Should().Be("dataQuality");
        drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    private static DqtRun CreateCompletedRun(int totalChecked, int totalMismatches)
    {
        var date = new DateOnly(2026, 5, 5);
        var run = DqtRun.Start(
            DqtTestType.IssuedInvoiceComparison,
            date,
            date,
            DqtTriggerType.Scheduled);
        run.Complete(totalChecked, totalMismatches);
        return run;
    }

    private static JsonDocument ToJsonDoc(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload));
}
