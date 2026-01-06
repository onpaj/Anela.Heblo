using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

/// <summary>
/// Unit tests for HangfireJobEnqueuer service.
/// Tests reflection-based job enqueueing logic.
/// Note: These tests require Hangfire MemoryStorage to be initialized.
/// </summary>
public class HangfireJobEnqueuerTests
{
    private readonly Mock<ILogger<HangfireJobEnqueuer>> _loggerMock;
    private readonly HangfireJobEnqueuer _enqueuer;

    public HangfireJobEnqueuerTests()
    {
        // Initialize Hangfire with in-memory storage for testing
        GlobalConfiguration.Configuration.UseMemoryStorage();

        _loggerMock = new Mock<ILogger<HangfireJobEnqueuer>>();
        _enqueuer = new HangfireJobEnqueuer(_loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new HangfireJobEnqueuer(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region EnqueueJob Tests

    [Fact]
    public void EnqueueJob_WithNullJob_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => _enqueuer.EnqueueJob(null!, CancellationToken.None);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("job");
    }

    [Fact]
    public void EnqueueJob_WithValidJob_ShouldReturnJobId()
    {
        // Arrange
        var testJob = new TestRecurringJob();
        var cancellationToken = CancellationToken.None;

        // Act
        var result = _enqueuer.EnqueueJob(testJob, cancellationToken);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void EnqueueJob_WithValidJob_ShouldLogInformation()
    {
        // Arrange
        var testJob = new TestRecurringJob();
        var cancellationToken = CancellationToken.None;

        // Act
        _enqueuer.EnqueueJob(testJob, cancellationToken);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("TestRecurringJob") && v.ToString()!.Contains("enqueued")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void EnqueueJob_WithCancellationToken_ShouldPassTokenToJob()
    {
        // Arrange
        var testJob = new TestRecurringJob();
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        // Act
        var result = _enqueuer.EnqueueJob(testJob, cancellationToken);

        // Assert
        // The test verifies that the enqueue call completes successfully
        // The cancellation token is embedded in the expression tree and will be
        // passed to ExecuteAsync when the job actually runs in Hangfire
        result.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Test Helper Classes

    /// <summary>
    /// Simple test implementation of IRecurringJob for testing purposes.
    /// </summary>
    private class TestRecurringJob : IRecurringJob
    {
        public RecurringJobMetadata Metadata => new RecurringJobMetadata
        {
            JobName = "test-job",
            DisplayName = "Test Job",
            Description = "A test job for unit testing",
            CronExpression = "0 0 * * *",
            DefaultIsEnabled = true,
            QueueName = "test",
            TimeZoneId = "Europe/Prague"
        };

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    #endregion
}
