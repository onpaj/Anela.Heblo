using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

/// <summary>
/// Integration test that verifies TriggerRecurringJobHandler works correctly
/// with actual Hangfire infrastructure (using in-memory storage).
///
/// This test ensures that the reflection-based job enqueueing works end-to-end
/// with real Hangfire components.
/// </summary>
public class TriggerRecurringJobHandlerIntegrationTests
{
    [Fact]
    public async Task Handle_WithRealHangfire_SuccessfullyEnqueuesJob()
    {
        // Arrange - Set up real Hangfire with in-memory storage
        GlobalConfiguration.Configuration.UseMemoryStorage();

        var mockStatusChecker = new Mock<IRecurringJobStatusChecker>();
        var mockHandlerLogger = new Mock<ILogger<TriggerRecurringJobHandler>>();
        var mockEnqueuerLogger = new Mock<ILogger<HangfireJobEnqueuer>>();

        mockStatusChecker
            .Setup(x => x.IsJobEnabledAsync("test-async-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var jobs = new List<IRecurringJob>
        {
            new TestAsyncRecurringJob("test-async-job")
        };

        var jobEnqueuer = new HangfireJobEnqueuer(mockEnqueuerLogger.Object);

        var handler = new TriggerRecurringJobHandler(
            jobs,
            mockStatusChecker.Object,
            jobEnqueuer,
            mockHandlerLogger.Object);

        var request = new TriggerRecurringJobRequest
        {
            JobName = "test-async-job",
            ForceDisabled = false
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.JobId);
        Assert.NotEmpty(result.JobId);

        // Verify status was checked
        mockStatusChecker.Verify(
            x => x.IsJobEnabledAsync("test-async-job", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithAsyncMethod_CreatesCorrectExpressionType()
    {
        // This test specifically verifies that we create Expression<Func<T, Task>>
        // not Expression<Action<T>>, which is the root cause of the error

        // Arrange
        GlobalConfiguration.Configuration.UseMemoryStorage();

        var mockStatusChecker = new Mock<IRecurringJobStatusChecker>();
        var mockHandlerLogger = new Mock<ILogger<TriggerRecurringJobHandler>>();
        var mockEnqueuerLogger = new Mock<ILogger<HangfireJobEnqueuer>>();

        mockStatusChecker
            .Setup(x => x.IsJobEnabledAsync("async-test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var jobs = new List<IRecurringJob>
        {
            new TestAsyncRecurringJob("async-test")
        };

        var jobEnqueuer = new HangfireJobEnqueuer(mockEnqueuerLogger.Object);

        var handler = new TriggerRecurringJobHandler(
            jobs,
            mockStatusChecker.Object,
            jobEnqueuer,
            mockHandlerLogger.Object);

        var request = new TriggerRecurringJobRequest
        {
            JobName = "async-test",
            ForceDisabled = false
        };

        // Act - This should NOT throw ArgumentException about Expression type mismatch
        Exception? caughtException = null;
        TriggerRecurringJobResponse? result = null;

        try
        {
            result = await handler.Handle(request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        // Assert
        Assert.Null(caughtException);
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.JobId);
    }

    [Fact]
    public async Task Handle_WithDisabledJob_CanBeTriggeredWithForceFlag()
    {
        // Arrange
        GlobalConfiguration.Configuration.UseMemoryStorage();

        var mockStatusChecker = new Mock<IRecurringJobStatusChecker>();
        var mockHandlerLogger = new Mock<ILogger<TriggerRecurringJobHandler>>();
        var mockEnqueuerLogger = new Mock<ILogger<HangfireJobEnqueuer>>();

        mockStatusChecker
            .Setup(x => x.IsJobEnabledAsync("disabled-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var jobs = new List<IRecurringJob>
        {
            new TestAsyncRecurringJob("disabled-job")
        };

        var jobEnqueuer = new HangfireJobEnqueuer(mockEnqueuerLogger.Object);

        var handler = new TriggerRecurringJobHandler(
            jobs,
            mockStatusChecker.Object,
            jobEnqueuer,
            mockHandlerLogger.Object);

        var request = new TriggerRecurringJobRequest
        {
            JobName = "disabled-job",
            ForceDisabled = true
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.JobId);
        Assert.NotEmpty(result.JobId);

        // Verify status check was skipped
        mockStatusChecker.Verify(
            x => x.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Test job that mimics real async recurring jobs
    /// </summary>
    private class TestAsyncRecurringJob : IRecurringJob
    {
        public RecurringJobMetadata Metadata { get; }

        public TestAsyncRecurringJob(string jobName)
        {
            Metadata = new RecurringJobMetadata
            {
                JobName = jobName,
                DisplayName = $"Test {jobName}",
                Description = "Test async job for integration testing",
                CronExpression = "0 0 * * *"
            };
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            // Simulate async work
            await Task.Delay(1, cancellationToken);
        }
    }
}
