using Anela.Heblo.Adapters.Flexi.Analytics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public sealed class FlexiAnalyticsSyncJobTests
{
    private static Mock<IFlexiAnalyticsSyncService> CreateSyncServiceMock() =>
        new();

    private static IOptions<FlexiAnalyticsSyncOptions> CreateOptions(bool enabled, int timeoutSeconds = 30) =>
        Options.Create(new FlexiAnalyticsSyncOptions
        {
            Enabled = enabled,
            RequestTimeoutSeconds = timeoutSeconds,
        });

    private static FlexiAnalyticsSyncJob CreateJob(
        IFlexiAnalyticsSyncService syncService,
        IOptions<FlexiAnalyticsSyncOptions> options,
        ILogger<FlexiAnalyticsSyncJob>? logger = null) =>
        new(syncService, options, logger ?? Mock.Of<ILogger<FlexiAnalyticsSyncJob>>());

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_CallsSyncAllAsync()
    {
        // Arrange
        var syncServiceMock = CreateSyncServiceMock();
        var report = new FlexiAnalyticsSyncReport(10, 8, 0, IsFullSuccess: true);
        syncServiceMock
            .Setup(s => s.SyncAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var job = CreateJob(syncServiceMock.Object, CreateOptions(enabled: true));

        // Act
        await job.ExecuteAsync();

        // Assert
        syncServiceMock.Verify(s => s.SyncAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_DoesNotCallSyncAllAsync()
    {
        // Arrange
        var syncServiceMock = CreateSyncServiceMock();

        var job = CreateJob(syncServiceMock.Object, CreateOptions(enabled: false));

        // Act
        await job.ExecuteAsync();

        // Assert
        syncServiceMock.Verify(s => s.SyncAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSyncServiceThrows_LogsErrorAndDoesNotRethrow()
    {
        // Arrange
        var syncServiceMock = CreateSyncServiceMock();
        syncServiceMock
            .Setup(s => s.SyncAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Flexi connection failed"));

        var loggerMock = new Mock<ILogger<FlexiAnalyticsSyncJob>>();
        var job = CreateJob(syncServiceMock.Object, CreateOptions(enabled: true), loggerMock.Object);

        // Act
        var act = () => job.ExecuteAsync();

        // Assert — exception must not propagate
        await act.Should().NotThrowAsync();

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => true),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
