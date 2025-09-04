using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.FinancialOverview.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Common;

// Test service classes for generic testing
public class TestService1 { }
public class TestService2 { }

public class BackgroundServiceReadinessTrackerTests
{
    private readonly Mock<ILogger<BackgroundServiceReadinessTracker>> _loggerMock;
    private readonly BackgroundServiceReadinessTracker _tracker;

    public BackgroundServiceReadinessTrackerTests()
    {
        _loggerMock = new Mock<ILogger<BackgroundServiceReadinessTracker>>();
        _tracker = new BackgroundServiceReadinessTracker(_loggerMock.Object);
    }

    [Fact]
    public void ReportInitialLoadCompleted_ShouldSetServiceAsReady()
    {
        // Act
        _tracker.ReportInitialLoadCompleted<TestService1>();

        // Assert
        Assert.True(_tracker.IsServiceReady<TestService1>());
    }

    [Fact]
    public void IsServiceReady_ShouldReturnFalseForUnknownService()
    {
        // Act & Assert
        Assert.False(_tracker.IsServiceReady<TestService1>());
    }

    [Fact]
    public void AreAllServicesReady_ShouldReturnFalseWhenCatalogServiceNotReady()
    {
        // Arrange
        _tracker.ReportInitialLoadCompleted<FinancialAnalysisBackgroundService>();

        // Act & Assert
        Assert.False(_tracker.AreAllServicesReady());
    }

    [Fact]
    public void AreAllServicesReady_ShouldReturnFalseWhenFinancialServiceNotReady()
    {
        // Arrange
        _tracker.ReportInitialLoadCompleted<CatalogRefreshBackgroundService>();

        // Act & Assert
        Assert.False(_tracker.AreAllServicesReady());
    }

    [Fact]
    public void AreAllServicesReady_ShouldReturnTrueWhenAllRequiredServicesReady()
    {
        // Arrange
        _tracker.ReportInitialLoadCompleted<CatalogRefreshBackgroundService>();
        _tracker.ReportInitialLoadCompleted<FinancialAnalysisBackgroundService>();

        // Act & Assert
        Assert.True(_tracker.AreAllServicesReady());
    }

    [Fact]
    public void GetServiceStatuses_ShouldReturnAllReportedServices()
    {
        // Arrange
        _tracker.ReportInitialLoadCompleted<TestService1>();
        _tracker.ReportInitialLoadCompleted<TestService2>();

        // Act
        var statuses = _tracker.GetServiceStatuses();

        // Assert
        Assert.Equal(2, statuses.Count);
        Assert.True(statuses["TestService1"]);
        Assert.True(statuses["TestService2"]);
    }

    [Fact]
    public void ReportInitialLoadCompleted_ShouldLogCorrectly()
    {
        // Act
        _tracker.ReportInitialLoadCompleted<TestService1>();

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Background service 'TestService1' reported initial load completion")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void TypeSafety_ShouldDistinguishBetweenDifferentServiceTypes()
    {
        // Arrange
        _tracker.ReportInitialLoadCompleted<TestService1>();

        // Act & Assert
        Assert.True(_tracker.IsServiceReady<TestService1>());
        Assert.False(_tracker.IsServiceReady<TestService2>());
    }
}