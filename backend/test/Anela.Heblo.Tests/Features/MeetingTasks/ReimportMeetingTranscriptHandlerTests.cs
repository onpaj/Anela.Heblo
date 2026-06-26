using System.Net.Http;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.ReimportMeetingTranscript;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class ReimportMeetingTranscriptHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _mockRepository;
    private readonly Mock<IPlaudClient> _mockPlaudClient;
    private readonly Mock<IMeetingAccessGuard> _mockAccessGuard;
    private readonly Mock<IMeetingTaskExtractor> _mockExtractor;
    private readonly Mock<IMeetingUserDirectory> _mockDirectory;
    private readonly Mock<ILogger<ReimportMeetingTranscriptHandler>> _mockLogger;
    private readonly ReimportMeetingTranscriptHandler _handler;

    public ReimportMeetingTranscriptHandlerTests()
    {
        _mockRepository = new Mock<IMeetingTranscriptRepository>();
        _mockPlaudClient = new Mock<IPlaudClient>();
        _mockAccessGuard = new Mock<IMeetingAccessGuard>();
        _mockExtractor = new Mock<IMeetingTaskExtractor>();
        _mockDirectory = new Mock<IMeetingUserDirectory>();
        _mockLogger = new Mock<ILogger<ReimportMeetingTranscriptHandler>>();

        _mockAccessGuard.Setup(g => g.CanAccess(It.IsAny<MeetingTranscript>())).Returns(true);

        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary>());

        _handler = new ReimportMeetingTranscriptHandler(
            _mockRepository.Object,
            _mockPlaudClient.Object,
            _mockAccessGuard.Object,
            _mockExtractor.Object,
            _mockDirectory.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WhenMeetingNotFound_ReturnsNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        // Act
        var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _mockPlaudClient.Verify(c => c.GetFileDetailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenRecordingNotGenerated_ReturnsBusinessRuleViolation()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_raw",
            Subject = "Old Subject",
            Summary = "Old summary",
            RawTranscript = "Old transcript",
            Tasks = new List<ProposedTask>
            {
                new() { Id = Guid.NewGuid(), MeetingTranscriptId = id, Title = "Existing Task" }
            }
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync("rec_raw", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = false, SummaryAvailable = false, AudioAvailable = false });

        // Act
        var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.BusinessRuleViolation);
        _mockRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenGenerated_RefreshesSummaryTranscriptAndSubject()
    {
        // Arrange
        var id = Guid.NewGuid();
        var existingTask = new ProposedTask { Id = Guid.NewGuid(), MeetingTranscriptId = id, Title = "Keep This Task" };
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_gen",
            Subject = "Old Subject",
            Summary = "Old summary",
            RawTranscript = "Old transcript",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow.AddDays(-1),
            Tasks = new List<ProposedTask> { existingTask }
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary>());

        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync("rec_gen", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });

        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync("rec_gen", It.IsAny<CancellationToken>()))
            .ReturnsAsync("New transcript content");

        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync("rec_gen", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("New Headline", "New summary content"));

        _mockExtractor
            .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>());

        _mockRepository
            .Setup(r => r.ReplacePendingTasksAsync(It.IsAny<MeetingTranscript>(), It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();

        entity.RawTranscript.Should().Be("New transcript content");
        entity.Summary.Should().Be("New summary content");
        entity.Subject.Should().Be("New Headline");

        _mockRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenHeadlineIsEmpty_PreservesExistingSubject()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_nohdr",
            Subject = "Original Subject",
            Summary = "Old summary",
            RawTranscript = "Old transcript",
            Tasks = new List<ProposedTask>()
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync("rec_nohdr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });

        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync("rec_nohdr", It.IsAny<CancellationToken>()))
            .ReturnsAsync("New transcript");

        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync("rec_nohdr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult(string.Empty, "New summary"));

        _mockExtractor
            .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>());

        _mockRepository
            .Setup(r => r.ReplacePendingTasksAsync(It.IsAny<MeetingTranscript>(), It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        entity.Subject.Should().Be("Original Subject");
    }

    [Fact]
    public async Task Handle_WhenAccessDenied_ReturnsNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new MeetingTranscript { Id = id, PlaudRecordingId = "rec_priv", Tasks = new List<ProposedTask>() };

        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _mockAccessGuard
            .Setup(g => g.CanAccess(entity))
            .Returns(false);

        // Act
        var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _mockPlaudClient.Verify(c => c.GetFileDetailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenGenerated_ReExtractsPendingTasksAndPreservesApproved()
    {
        // Arrange
        var id = Guid.NewGuid();
        var pendingTask = new ProposedTask
        {
            Id = Guid.NewGuid(),
            MeetingTranscriptId = id,
            Title = "Old Pending",
            Status = ProposedTaskStatus.Pending
        };
        var approvedTask = new ProposedTask
        {
            Id = Guid.NewGuid(),
            MeetingTranscriptId = id,
            Title = "Already Approved",
            Status = ProposedTaskStatus.Approved
        };
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_reextract",
            Subject = "Subject",
            Summary = "Old summary",
            RawTranscript = "Old transcript",
            Tasks = new List<ProposedTask> { pendingTask, approvedTask }
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync("rec_reextract", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync("rec_reextract", It.IsAny<CancellationToken>()))
            .ReturnsAsync("New transcript");
        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync("rec_reextract", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("New Headline", "New summary"));

        _mockExtractor
            .Setup(e => e.ExtractAsync("New summary", "New transcript", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>
            {
                new("New Task 1", "Description 1", "Ondra", null, "ondra@anela.cz")
            });

        IReadOnlyList<ProposedTask>? capturedNewTasks = null;
        _mockRepository
            .Setup(r => r.ReplacePendingTasksAsync(entity, It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
            .Callback<MeetingTranscript, IReadOnlyList<ProposedTask>, CancellationToken>((_, tasks, _) => capturedNewTasks = tasks)
            .Returns(Task.CompletedTask);
        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        capturedNewTasks.Should().HaveCount(1);
        capturedNewTasks!.Single().Title.Should().Be("New Task 1");
        capturedNewTasks.Single().AssigneeEmail.Should().Be("ondra@anela.cz");
        capturedNewTasks.Single().Status.Should().Be(ProposedTaskStatus.Pending);
        capturedNewTasks.Single().IsManuallyAdded.Should().BeFalse();
        capturedNewTasks.Should().NotContain(t => t.Id == approvedTask.Id);
    }

    [Fact]
    public async Task Handle_WhenExtractionReturnsEmpty_ReplacePendingCalledWithEmptyList()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_empty_extract",
            Subject = "Subject",
            Summary = "Old summary",
            RawTranscript = "Old transcript",
            Tasks = new List<ProposedTask>()
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync("rec_empty_extract", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync("rec_empty_extract", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Transcript");
        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync("rec_empty_extract", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("Headline", "Summary"));
        _mockExtractor
            .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>());

        IReadOnlyList<ProposedTask>? capturedNewTasks = null;
        _mockRepository
            .Setup(r => r.ReplacePendingTasksAsync(It.IsAny<MeetingTranscript>(), It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
            .Callback<MeetingTranscript, IReadOnlyList<ProposedTask>, CancellationToken>((_, tasks, _) => capturedNewTasks = tasks)
            .Returns(Task.CompletedTask);
        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        capturedNewTasks.Should().NotBeNull();
        capturedNewTasks.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenExtractedTaskHasNoEmail_ResolvesFromDirectory()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_resolve",
            Subject = "Subject",
            Summary = "Old",
            RawTranscript = "Old",
            Tasks = new List<ProposedTask>()
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync("rec_resolve", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync("rec_resolve", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Transcript");
        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync("rec_resolve", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("Headline", "Summary"));
        _mockExtractor
            .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>
            {
                new("Task", "Desc", "Andy", null, AssigneeEmail: null)
            });
        _mockDirectory
            .Setup(d => d.Resolve("Andy"))
            .Returns(new MeetingUser("andrea@anela.cz", "Andrea Pajgrt Bartošová", new[] { "Andy" }));

        IReadOnlyList<ProposedTask>? capturedNewTasks = null;
        _mockRepository
            .Setup(r => r.ReplacePendingTasksAsync(It.IsAny<MeetingTranscript>(), It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
            .Callback<MeetingTranscript, IReadOnlyList<ProposedTask>, CancellationToken>((_, tasks, _) => capturedNewTasks = tasks)
            .Returns(Task.CompletedTask);
        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        capturedNewTasks!.Single().AssigneeEmail.Should().Be("andrea@anela.cz");
    }

    [Fact]
    public async Task Handle_WhenRecordingNamePresent_UsesRecordingNameAsSubject()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_named",
            PlaudCreatedAt = DateTime.UtcNow.AddDays(-2),
            Subject = "Old Subject",
            Summary = "Old summary",
            RawTranscript = "Old transcript",
            Tasks = new List<ProposedTask>()
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync("rec_named", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync("rec_named", It.IsAny<CancellationToken>()))
            .ReturnsAsync("New transcript");
        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync("rec_named", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("Auto Headline", "New summary"));
        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary>
            {
                new() { Id = "rec_named", Name = "Týmová porada: letní plány", CreatedAt = entity.PlaudCreatedAt }
            });
        _mockExtractor
            .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>());
        _mockRepository
            .Setup(r => r.ReplacePendingTasksAsync(It.IsAny<MeetingTranscript>(), It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        entity.Subject.Should().Be("Týmová porada: letní plány");
        _mockRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenRecordingNameEmpty_UsesHeadlineAsSubject()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_noname",
            PlaudCreatedAt = DateTime.UtcNow.AddDays(-1),
            Subject = "Old Subject",
            Summary = "Old summary",
            RawTranscript = "Old transcript",
            Tasks = new List<ProposedTask>()
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync("rec_noname", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync("rec_noname", It.IsAny<CancellationToken>()))
            .ReturnsAsync("New transcript");
        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync("rec_noname", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("Auto Headline", "New summary"));
        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary>
            {
                new() { Id = "rec_noname", Name = string.Empty, CreatedAt = entity.PlaudCreatedAt }
            });
        _mockExtractor
            .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>());
        _mockRepository
            .Setup(r => r.ReplacePendingTasksAsync(It.IsAny<MeetingTranscript>(), It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        entity.Subject.Should().Be("Auto Headline");
    }

    [Fact]
    public async Task Handle_WhenListRecentThrows_StillSucceedsAndFallsBackToHeadline()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_apierr",
            PlaudCreatedAt = DateTime.UtcNow.AddDays(-1),
            Subject = "Old Subject",
            Summary = "Old summary",
            RawTranscript = "Old transcript",
            Tasks = new List<ProposedTask>()
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync("rec_apierr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync("rec_apierr", It.IsAny<CancellationToken>()))
            .ReturnsAsync("New transcript");
        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync("rec_apierr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("Fallback Headline", "New summary"));
        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Plaud API unavailable"));
        _mockExtractor
            .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>());
        _mockRepository
            .Setup(r => r.ReplacePendingTasksAsync(It.IsAny<MeetingTranscript>(), It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        entity.RawTranscript.Should().Be("New transcript");
        entity.Summary.Should().Be("New summary");
        entity.Subject.Should().Be("Fallback Headline");
    }
}
