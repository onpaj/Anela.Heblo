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
    public void AreAllServicesReady_ShouldReturnFalseByDefault()
    {
        // Act & Assert - Hydration not completed by default
        Assert.False(_tracker.AreAllServicesReady());
    }

    [Fact]
    public void AreAllServicesReady_ShouldReturnTrueAfterHydrationCompleted()
    {
        // Arrange
        _tracker.ReportHydrationCompleted();

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
        Assert.Equal(3, statuses.Count); // 2 services + TierBasedHydration
        Assert.True(statuses["TestService1"]);
        Assert.True(statuses["TestService2"]);
        Assert.False(statuses["TierBasedHydration"]); // Should be false by default
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

    [Fact]
    public void GetHydrationDetails_ShouldReturnCorrectInitialState()
    {
        // Act
        var details = _tracker.GetHydrationDetails();

        // Assert
        Assert.False((bool)details["IsCompleted"]);
        Assert.Equal("Not started", details["StartedAt"]);
        Assert.Equal("Not completed", details["CompletedAt"]);
        Assert.False(details.ContainsKey("FailureReason"));
    }

    [Fact]
    public void ReportHydrationCompleted_ShouldUpdateStatusesCorrectly()
    {
        // Arrange
        _tracker.ReportHydrationStarted();

        // Act
        _tracker.ReportHydrationCompleted();

        // Assert
        Assert.True(_tracker.AreAllServicesReady());
        var statuses = _tracker.GetServiceStatuses();
        Assert.True(statuses["TierBasedHydration"]);

        var details = _tracker.GetHydrationDetails();
        Assert.True((bool)details["IsCompleted"]);
        Assert.NotEqual("Not completed", details["CompletedAt"]);
    }

    [Fact]
    public void ReportHydrationFailed_ShouldSetFailureState()
    {
        // Arrange
        _tracker.ReportHydrationStarted();
        var failureReason = "Test failure";

        // Act
        _tracker.ReportHydrationFailed(failureReason);

        // Assert
        Assert.False(_tracker.AreAllServicesReady());
        var details = _tracker.GetHydrationDetails();
        Assert.False((bool)details["IsCompleted"]);
        Assert.Equal(failureReason, details["FailureReason"]);
    }
}