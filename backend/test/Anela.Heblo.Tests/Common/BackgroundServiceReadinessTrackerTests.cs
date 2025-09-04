using Anela.Heblo.Application.Common;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Anela.Heblo.Tests.Common;

public class BackgroundServiceReadinessTrackerTests
{
    private readonly ILogger<BackgroundServiceReadinessTracker> _logger;
    private readonly BackgroundServiceReadinessTracker _tracker;

    public BackgroundServiceReadinessTrackerTests()
    {
        _logger = Substitute.For<ILogger<BackgroundServiceReadinessTracker>>();
        _tracker = new BackgroundServiceReadinessTracker(_logger);
    }

    [Fact]
    public void ReportInitialLoadCompleted_ShouldSetServiceAsReady()
    {
        // Arrange
        var serviceName = "TestService";

        // Act
        _tracker.ReportInitialLoadCompleted(serviceName);

        // Assert
        Assert.True(_tracker.IsServiceReady(serviceName));
    }

    [Fact]
    public void IsServiceReady_ShouldReturnFalseForUnknownService()
    {
        // Act & Assert
        Assert.False(_tracker.IsServiceReady("UnknownService"));
    }

    [Fact]
    public void AreAllServicesReady_ShouldReturnFalseWhenCatalogServiceNotReady()
    {
        // Arrange
        _tracker.ReportInitialLoadCompleted("FinancialAnalysisBackgroundService");

        // Act & Assert
        Assert.False(_tracker.AreAllServicesReady());
    }

    [Fact]
    public void AreAllServicesReady_ShouldReturnFalseWhenFinancialServiceNotReady()
    {
        // Arrange
        _tracker.ReportInitialLoadCompleted("CatalogRefreshBackgroundService");

        // Act & Assert
        Assert.False(_tracker.AreAllServicesReady());
    }

    [Fact]
    public void AreAllServicesReady_ShouldReturnTrueWhenAllRequiredServicesReady()
    {
        // Arrange
        _tracker.ReportInitialLoadCompleted("CatalogRefreshBackgroundService");
        _tracker.ReportInitialLoadCompleted("FinancialAnalysisBackgroundService");

        // Act & Assert
        Assert.True(_tracker.AreAllServicesReady());
    }

    [Fact]
    public void GetServiceStatuses_ShouldReturnAllReportedServices()
    {
        // Arrange
        _tracker.ReportInitialLoadCompleted("Service1");
        _tracker.ReportInitialLoadCompleted("Service2");

        // Act
        var statuses = _tracker.GetServiceStatuses();

        // Assert
        Assert.Equal(2, statuses.Count);
        Assert.True(statuses["Service1"]);
        Assert.True(statuses["Service2"]);
    }

    [Fact]
    public void ReportInitialLoadCompleted_ShouldLogCorrectly()
    {
        // Arrange
        var serviceName = "TestService";

        // Act
        _tracker.ReportInitialLoadCompleted(serviceName);

        // Assert
        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains($"Background service '{serviceName}' reported initial load completion")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}