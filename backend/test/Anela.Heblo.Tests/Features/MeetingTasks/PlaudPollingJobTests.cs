using Anela.Heblo.Application.Features.MeetingTasks;
using Anela.Heblo.Application.Features.MeetingTasks.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class PlaudPollingJobTests
{
    private readonly Mock<IPlaudClient> _mockPlaudClient = new();
    private readonly Mock<IMediator> _mockMediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _mockStatusChecker = new();
    private readonly Mock<ILogger<PlaudPollingJob>> _mockLogger = new();
    private readonly PlaudPollingHealthTracker _tracker = new();
    private readonly PlaudPollingJob _job;

    public PlaudPollingJobTests()
    {
        _mockStatusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-polling", It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(true);

        _job = CreateJob(12);
    }

    private PlaudPollingJob CreateJob(int alertThreshold) => new(
        _mockPlaudClient.Object,
        _mockMediator.Object,
        _mockStatusChecker.Object,
        Options.Create(new MeetingTasksOptions { MaxRecordingAgeDays = 7, UpstreamFailureAlertThreshold = alertThreshold }),
        _tracker,
        _mockLogger.Object);

    private static PlaudUpstreamUnavailableException Upstream() => new(500, "upstream 500");

    private static PlaudRecordingSummary Rec(string id) => new()
    {
        Id = id,
        Name = id,
        CreatedAt = new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc)
    };

    private void VerifyLog(LogLevel level, Times times) =>
        _mockLogger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    [Fact]
    public async Task ExecuteAsync_WhenJobDisabled_SkipsWithoutCallingPlaudOrMediator()
    {
        // Arrange
        _mockStatusChecker
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(false);

        // Act
        var act = async () => await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();

        _mockPlaudClient.Verify(
            c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockMediator.Verify(
            m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRecordingSkippedAndNotGenerated_LogsNotGeneratedCount()
    {
        // Arrange
        var recording = new PlaudRecordingSummary
        {
            Id = "rec-1",
            Name = "Weekly sync",
            CreatedAt = new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc)
        };

        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary> { recording });

        _mockMediator
            .Setup(m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestPlaudRecordingResponse { Skipped = true, NotGenerated = true });

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        _mockMediator.Verify(
            m => m.Send(
                It.Is<IngestPlaudRecordingRequest>(r =>
                    r.PlaudRecordingId == "rec-1" &&
                    r.Name == "Weekly sync" &&
                    r.PlaudCreatedAt == recording.CreatedAt),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(
                    "0 new recordings ingested, 0 already known, 1 not yet generated")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRecordingSkippedAndAlreadyKnown_LogsSkippedCount()
    {
        // Arrange
        var recording = new PlaudRecordingSummary
        {
            Id = "rec-2",
            Name = "Standup",
            CreatedAt = new DateTime(2026, 8, 11, 9, 0, 0, DateTimeKind.Utc)
        };

        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary> { recording });

        _mockMediator
            .Setup(m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestPlaudRecordingResponse { Skipped = true, NotGenerated = false });

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        _mockMediator.Verify(
            m => m.Send(
                It.Is<IngestPlaudRecordingRequest>(r =>
                    r.PlaudRecordingId == "rec-2" &&
                    r.Name == "Standup" &&
                    r.PlaudCreatedAt == recording.CreatedAt),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(
                    "0 new recordings ingested, 1 already known, 0 not yet generated")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRecordingIngested_LogsIngestedCount()
    {
        // Arrange
        var recording = new PlaudRecordingSummary
        {
            Id = "rec-3",
            Name = "Planning",
            CreatedAt = new DateTime(2026, 8, 12, 9, 0, 0, DateTimeKind.Utc)
        };

        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary> { recording });

        _mockMediator
            .Setup(m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestPlaudRecordingResponse { Skipped = false });

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        _mockMediator.Verify(
            m => m.Send(
                It.Is<IngestPlaudRecordingRequest>(r =>
                    r.PlaudRecordingId == "rec-3" &&
                    r.Name == "Planning" &&
                    r.PlaudCreatedAt == recording.CreatedAt),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(
                    "1 new recordings ingested, 0 already known, 0 not yet generated")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMediatorThrowsForOneRecording_ContinuesProcessingRemainingRecordings()
    {
        // Arrange
        var failingRecording = new PlaudRecordingSummary
        {
            Id = "rec-fail",
            Name = "Broken meeting",
            CreatedAt = new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc)
        };
        var survivingRecording = new PlaudRecordingSummary
        {
            Id = "rec-ok",
            Name = "Good meeting",
            CreatedAt = new DateTime(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc)
        };

        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary> { failingRecording, survivingRecording });

        var thrownException = new InvalidOperationException("Mediator pipeline failure");

        _mockMediator
            .Setup(m => m.Send(
                It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "rec-fail"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrownException);

        _mockMediator
            .Setup(m => m.Send(
                It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "rec-ok"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestPlaudRecordingResponse { Skipped = false });

        // Act
        var act = async () => await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();

        _mockMediator.Verify(
            m => m.Send(
                It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "rec-fail"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockMediator.Verify(
            m => m.Send(
                It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "rec-ok"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Failed to ingest recording rec-fail")),
                It.Is<Exception>(ex => ex == thrownException),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(
                    "1 new recordings ingested, 0 already known, 0 not yet generated")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenListThrowsUpstreamUnavailable_DoesNotThrowAndLogsWarning()
    {
        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Upstream());

        var act = async () => await _job.ExecuteAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        _mockMediator.Verify(
            m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _tracker.ConsecutiveFailures.Should().Be(1);
        VerifyLog(LogLevel.Warning, Times.Once());
        VerifyLog(LogLevel.Error, Times.Never());
    }

    [Fact]
    public async Task ExecuteAsync_WhenThresholdReached_LogsSingleErrorOnThresholdRun()
    {
        var job = CreateJob(2);
        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Upstream());

        await job.ExecuteAsync(CancellationToken.None);
        VerifyLog(LogLevel.Error, Times.Never());

        await job.ExecuteAsync(CancellationToken.None);
        VerifyLog(LogLevel.Error, Times.Once());

        await job.ExecuteAsync(CancellationToken.None);
        VerifyLog(LogLevel.Error, Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_WhenThresholdIsZero_NeverLogsError()
    {
        var job = CreateJob(0);
        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Upstream());

        for (var i = 0; i < 5; i++)
            await job.ExecuteAsync(CancellationToken.None);

        VerifyLog(LogLevel.Error, Times.Never());
        VerifyLog(LogLevel.Warning, Times.Exactly(5));
    }

    [Fact]
    public async Task ExecuteAsync_WhenListSucceeds_ResetsTracker()
    {
        _tracker.RecordUpstreamFailure();
        _tracker.RecordUpstreamFailure();
        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary>());

        await _job.ExecuteAsync(CancellationToken.None);

        _tracker.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMediatorThrowsUpstreamUnavailable_StopsLoopWithoutThrowing()
    {
        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary> { Rec("a"), Rec("b"), Rec("c") });
        _mockMediator
            .Setup(m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Upstream());

        var act = async () => await _job.ExecuteAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        _mockMediator.Verify(
            m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _tracker.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMediatorThrowsPlainException_ProcessesRemainingRecordings()
    {
        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary> { Rec("a"), Rec("b"), Rec("c") });
        _mockMediator
            .SetupSequence(m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"))
            .ReturnsAsync(new IngestPlaudRecordingResponse { Skipped = false })
            .ReturnsAsync(new IngestPlaudRecordingResponse { Skipped = false });

        await _job.ExecuteAsync(CancellationToken.None);

        _mockMediator.Verify(
            m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        _tracker.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenListThrowsOtherInvalidOperation_Propagates()
    {
        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("auth expired"));

        var act = async () => await _job.ExecuteAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("auth expired");
        _tracker.ConsecutiveFailures.Should().Be(0);
    }
}
