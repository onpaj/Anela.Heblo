using Anela.Heblo.Application.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

/// <summary>
/// Integration test that verifies RecurringJobTriggerService works correctly
/// with actual Hangfire infrastructure (using in-memory storage).
///
/// This test ensures that the reflection-based job enqueueing works end-to-end
/// with real Hangfire components.
/// </summary>
public class RecurringJobTriggerServiceIntegrationTest
{
    [Fact]
    public async Task TriggerJobAsync_WithRealHangfire_SuccessfullyEnqueuesJob()
    {
        // Arrange - Set up real Hangfire with in-memory storage
        GlobalConfiguration.Configuration.UseMemoryStorage();

        var mockStatusChecker = new Mock<IRecurringJobStatusChecker>();
        var mockLogger = new Mock<ILogger<RecurringJobTriggerService>>();

        mockStatusChecker
            .Setup(x => x.IsJobEnabledAsync("test-async-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var services = new ServiceCollection();

        // Register the actual BackgroundJobClient
        services.AddSingleton<IBackgroundJobClient>(new BackgroundJobClient());

        // Register a test job
        services.AddSingleton<IRecurringJob>(new TestAsyncRecurringJob("test-async-job"));

        var serviceProvider = services.BuildServiceProvider();

        var service = new RecurringJobTriggerService(
            serviceProvider,
            mockStatusChecker.Object,
            mockLogger.Object);

        // Act
        var result = await service.TriggerJobAsync("test-async-job");

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);

        // Verify status was checked
        mockStatusChecker.Verify(
            x => x.IsJobEnabledAsync("test-async-job", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TriggerJobAsync_WithAsyncMethod_CreatesCorrectExpressionType()
    {
        // This test specifically verifies that we create Expression<Func<T, Task>>
        // not Expression<Action<T>>, which is the root cause of the error

        // Arrange
        GlobalConfiguration.Configuration.UseMemoryStorage();

        var mockStatusChecker = new Mock<IRecurringJobStatusChecker>();
        var mockLogger = new Mock<ILogger<RecurringJobTriggerService>>();

        mockStatusChecker
            .Setup(x => x.IsJobEnabledAsync("async-test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var services = new ServiceCollection();
        services.AddSingleton<IBackgroundJobClient>(new BackgroundJobClient());
        services.AddSingleton<IRecurringJob>(new TestAsyncRecurringJob("async-test"));

        var serviceProvider = services.BuildServiceProvider();

        var service = new RecurringJobTriggerService(
            serviceProvider,
            mockStatusChecker.Object,
            mockLogger.Object);

        // Act - This should NOT throw ArgumentException about Expression type mismatch
        Exception? caughtException = null;
        string? result = null;

        try
        {
            result = await service.TriggerJobAsync("async-test");
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        // Assert
        Assert.Null(caughtException);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task TriggerJobAsync_WithDisabledJob_CanBeTriggeredWithForceFlag()
    {
        // Arrange
        GlobalConfiguration.Configuration.UseMemoryStorage();

        var mockStatusChecker = new Mock<IRecurringJobStatusChecker>();
        var mockLogger = new Mock<ILogger<RecurringJobTriggerService>>();

        mockStatusChecker
            .Setup(x => x.IsJobEnabledAsync("disabled-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var services = new ServiceCollection();
        services.AddSingleton<IBackgroundJobClient>(new BackgroundJobClient());
        services.AddSingleton<IRecurringJob>(new TestAsyncRecurringJob("disabled-job"));

        var serviceProvider = services.BuildServiceProvider();

        var service = new RecurringJobTriggerService(
            serviceProvider,
            mockStatusChecker.Object,
            mockLogger.Object);

        // Act
        var result = await service.TriggerJobAsync("disabled-job", forceDisabled: true);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);

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
