using Anela.Heblo.Application.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class RecurringJobTriggerServiceTests
{
    private readonly Mock<IRecurringJobManager> _mockRecurringJobManager;
    private readonly Mock<IRecurringJobStatusChecker> _mockStatusChecker;
    private readonly RecurringJobTriggerService _service;

    public RecurringJobTriggerServiceTests()
    {
        _mockRecurringJobManager = new Mock<IRecurringJobManager>();
        _mockStatusChecker = new Mock<IRecurringJobStatusChecker>();
        _service = new RecurringJobTriggerService(_mockRecurringJobManager.Object, _mockStatusChecker.Object);
    }

    [Fact]
    public async Task TriggerJobAsync_WhenJobIsEnabled_TriggersJobExecution()
    {
        // Arrange
        var jobName = "test-job";
        _mockStatusChecker
            .Setup(x => x.IsJobEnabledAsync(jobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.TriggerJobAsync(jobName);

        // Assert
        _mockRecurringJobManager.Verify(
            x => x.Trigger(jobName),
            Times.Once);
        Assert.NotNull(result);
        Assert.Equal(jobName, result);
    }

    [Fact]
    public async Task TriggerJobAsync_WhenJobIsDisabled_ReturnsNull()
    {
        // Arrange
        var jobName = "test-job";
        _mockStatusChecker
            .Setup(x => x.IsJobEnabledAsync(jobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _service.TriggerJobAsync(jobName);

        // Assert
        _mockRecurringJobManager.Verify(
            x => x.Trigger(It.IsAny<string>()),
            Times.Never);
        Assert.Null(result);
    }

    [Fact]
    public async Task TriggerJobAsync_WithForceDisabledTrue_DoesNotCheckStatusAndReturnsNull()
    {
        // Arrange
        var jobName = "test-job";

        // Act
        var result = await _service.TriggerJobAsync(jobName, forceDisabled: true);

        // Assert
        _mockStatusChecker.Verify(
            x => x.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRecurringJobManager.Verify(
            x => x.Trigger(It.IsAny<string>()),
            Times.Never);
        Assert.Null(result);
    }

    [Fact]
    public async Task TriggerJobAsync_WithForceDisabledFalse_ChecksStatusNormally()
    {
        // Arrange
        var jobName = "test-job";
        _mockStatusChecker
            .Setup(x => x.IsJobEnabledAsync(jobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.TriggerJobAsync(jobName, forceDisabled: false);

        // Assert
        _mockStatusChecker.Verify(
            x => x.IsJobEnabledAsync(jobName, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRecurringJobManager.Verify(
            x => x.Trigger(jobName),
            Times.Once);
        Assert.NotNull(result);
    }
}
