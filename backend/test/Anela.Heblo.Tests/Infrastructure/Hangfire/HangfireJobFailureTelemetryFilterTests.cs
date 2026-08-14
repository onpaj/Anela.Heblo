using Anela.Heblo.API.Infrastructure.Hangfire;
using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure.Hangfire;

public class HangfireJobFailureTelemetryFilterTests
{
    private readonly Mock<ITelemetryService> _telemetryService = new();
    private readonly Mock<JobStorage> _storage = new();
    private readonly Mock<IStorageConnection> _connection = new();
    private readonly Mock<IWriteOnlyTransaction> _transaction = new();

    private HangfireJobFailureTelemetryFilter CreateFilter() =>
        new(_telemetryService.Object, NullLogger<HangfireJobFailureTelemetryFilter>.Instance);

    private ApplyStateContext CreateContext(IState newState, string jobId = "job-42")
    {
        var job = Job.FromExpression(() => SampleJobMethod());
        var backgroundJob = new BackgroundJob(jobId, job, DateTime.UtcNow);
        return new ApplyStateContext(
            _storage.Object,
            _connection.Object,
            _transaction.Object,
            backgroundJob,
            newState,
            oldStateName: ProcessingState.StateName);
    }

    [Fact]
    public static void SampleJobMethod() { }

    [Fact]
    public void OnStateApplied_TracksEventAndException_WhenJobEntersFailedState()
    {
        // Arrange
        var exception = new InvalidOperationException("Something is missing");
        Dictionary<string, string>? eventProperties = null;
        _telemetryService
            .Setup(t => t.TrackBusinessEvent(
                "HangfireJobFailed",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, double>>()))
            .Callback<string, Dictionary<string, string>?, Dictionary<string, double>?>(
                (_, props, _) => eventProperties = props);

        // Act
        CreateFilter().OnStateApplied(CreateContext(new FailedState(exception)), _transaction.Object);

        // Assert
        eventProperties.Should().NotBeNull();
        eventProperties!["JobId"].Should().Be("job-42");
        eventProperties["JobType"].Should().Contain(nameof(HangfireJobFailureTelemetryFilterTests));
        eventProperties["JobMethod"].Should().Be(nameof(SampleJobMethod));
        eventProperties["ExceptionType"].Should().Contain(nameof(InvalidOperationException));
        eventProperties["ExceptionMessage"].Should().Be("Something is missing");

        _telemetryService.Verify(
            t => t.TrackException(exception, It.IsAny<Dictionary<string, string>>()),
            Times.Once);
    }

    [Fact]
    public void OnStateApplied_IncludesRecurringJobId_WhenJobBelongsToRecurringJob()
    {
        // Arrange
        _connection
            .Setup(c => c.GetJobParameter("job-42", "RecurringJobId"))
            .Returns("\"catalog-sync\"");
        Dictionary<string, string>? eventProperties = null;
        _telemetryService
            .Setup(t => t.TrackBusinessEvent(
                "HangfireJobFailed",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, double>>()))
            .Callback<string, Dictionary<string, string>?, Dictionary<string, double>?>(
                (_, props, _) => eventProperties = props);

        // Act
        CreateFilter().OnStateApplied(CreateContext(new FailedState(new Exception("boom"))), _transaction.Object);

        // Assert
        eventProperties.Should().NotBeNull();
        eventProperties!["RecurringJobId"].Should().Be("catalog-sync");
    }

    [Fact]
    public void OnStateApplied_IncludesRetryCount_WhenJobHasBeenRetried()
    {
        // Arrange
        _connection
            .Setup(c => c.GetJobParameter("job-42", "RetryCount"))
            .Returns("3");
        Dictionary<string, string>? eventProperties = null;
        _telemetryService
            .Setup(t => t.TrackBusinessEvent(
                "HangfireJobFailed",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, double>>()))
            .Callback<string, Dictionary<string, string>?, Dictionary<string, double>?>(
                (_, props, _) => eventProperties = props);

        // Act
        CreateFilter().OnStateApplied(CreateContext(new FailedState(new Exception("boom"))), _transaction.Object);

        // Assert
        eventProperties.Should().NotBeNull();
        eventProperties!["RetryCount"].Should().Be("3");
    }

    [Fact]
    public void OnStateApplied_DoesNotTrack_WhenStateIsNotFailed()
    {
        // Act
        CreateFilter().OnStateApplied(CreateContext(new EnqueuedState()), _transaction.Object);

        // Assert
        _telemetryService.Verify(
            t => t.TrackBusinessEvent(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, double>>()),
            Times.Never);
        _telemetryService.Verify(
            t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()),
            Times.Never);
    }

    [Fact]
    public void OnStateApplied_DoesNotThrow_WhenTelemetryServiceFails()
    {
        // Arrange
        _telemetryService
            .Setup(t => t.TrackBusinessEvent(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, double>>()))
            .Throws(new InvalidOperationException("telemetry channel down"));

        // Act
        var act = () => CreateFilter().OnStateApplied(CreateContext(new FailedState(new Exception("boom"))), _transaction.Object);

        // Assert
        act.Should().NotThrow();
    }
}
