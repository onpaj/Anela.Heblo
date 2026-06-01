using Anela.Heblo.Application.Features.Analytics.DashboardTiles;
using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics.DashboardTiles;

public class InvoiceImportStatisticsTileTests
{
    private readonly Mock<IAnalyticsRepository> _analyticsRepositoryMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly InvoiceImportStatisticsTile _tile;
    private readonly DateTime _fixedDateTime = new DateTime(2025, 10, 14, 10, 0, 0, DateTimeKind.Utc);

    public InvoiceImportStatisticsTileTests()
    {
        _analyticsRepositoryMock = new Mock<IAnalyticsRepository>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(_fixedDateTime);
        _tile = new InvoiceImportStatisticsTile(_analyticsRepositoryMock.Object, _timeProviderMock.Object);
    }

    [Fact]
    public async Task LoadDataAsync_WithDateParameter_UsesParameterDate()
    {
        // Arrange
        var targetDate = DateOnly.Parse("2025-10-13");
        var parameters = new Dictionary<string, string>
        {
            { "date", targetDate.ToString("yyyy-MM-dd") }
        };

        var mockStatistics = new List<DailyInvoiceCount>
        {
            new DailyInvoiceCount
            {
                Date = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Count = 5,
                IsBelowThreshold = false
            }
        };

        _analyticsRepositoryMock
            .Setup(x => x.GetInvoiceImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                ImportDateType.LastSyncTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockStatistics);

        // Act
        var result = await _tile.LoadDataAsync(parameters);

        // Assert
        result.Should().NotBeNull();

        // Verify that the repository was called with the correct UTC date range
        _analyticsRepositoryMock.Verify(x => x.GetInvoiceImportStatisticsAsync(
            targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            targetDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
            ImportDateType.LastSyncTime,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadDataAsync_WithoutDateParameter_UsesYesterdayUtcFallback()
    {
        // Arrange - no parameters provided
        var expectedYesterday = DateOnly.FromDateTime(_fixedDateTime.Date).AddDays(-1);

        var mockStatistics = new List<DailyInvoiceCount>
        {
            new DailyInvoiceCount
            {
                Date = expectedYesterday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Count = 3,
                IsBelowThreshold = false
            }
        };

        _analyticsRepositoryMock
            .Setup(x => x.GetInvoiceImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                ImportDateType.LastSyncTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockStatistics);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        result.Should().NotBeNull();

        // Verify that the repository was called with yesterday's UTC date range
        _analyticsRepositoryMock.Verify(x => x.GetInvoiceImportStatisticsAsync(
            expectedYesterday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            expectedYesterday.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
            ImportDateType.LastSyncTime,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadDataAsync_WithInvalidDateParameter_UsesFallback()
    {
        // Arrange
        var parameters = new Dictionary<string, string>
        {
            { "date", "invalid-date" }
        };

        var expectedYesterday = DateOnly.FromDateTime(_fixedDateTime.Date).AddDays(-1);

        var mockStatistics = new List<DailyInvoiceCount>();

        _analyticsRepositoryMock
            .Setup(x => x.GetInvoiceImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                ImportDateType.LastSyncTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockStatistics);

        // Act
        var result = await _tile.LoadDataAsync(parameters);

        // Assert
        result.Should().NotBeNull();

        // Verify that it fell back to yesterday UTC
        _analyticsRepositoryMock.Verify(x => x.GetInvoiceImportStatisticsAsync(
            expectedYesterday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            expectedYesterday.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
            ImportDateType.LastSyncTime,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}