using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;
using Anela.Heblo.Domain.Features.MeetingTasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;


namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class IngestPlaudRecordingHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _mockRepository;
    private readonly Mock<IPlaudClient> _mockPlaudClient;
    private readonly Mock<IMeetingTaskExtractor> _mockExtractor;
    private readonly Mock<IMeetingUserDirectory> _mockDirectory;
    private readonly Mock<ILogger<IngestPlaudRecordingHandler>> _mockLogger;
    private readonly IngestPlaudRecordingHandler _handler;

    public IngestPlaudRecordingHandlerTests()
    {
        _mockRepository = new Mock<IMeetingTranscriptRepository>();
        _mockPlaudClient = new Mock<IPlaudClient>();
        _mockExtractor = new Mock<IMeetingTaskExtractor>();
        _mockLogger = new Mock<ILogger<IngestPlaudRecordingHandler>>();
        _mockDirectory = new Mock<IMeetingUserDirectory>();

        _handler = new IngestPlaudRecordingHandler(
            _mockRepository.Object,
            _mockPlaudClient.Object,
            _mockExtractor.Object,
            _mockDirectory.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WithNewRecording_CreatesTranscriptAndTasksInPendingReviewState()
    {
        // Arrange
        var recordingId = "rec_001";
        var recordingName = "Test Meeting";
        var plaudCreatedAt = DateTime.UtcNow;
        const string transcript = "transcript text";
        const string summary = "summary text";

        var extractedTasks = new List<ExtractedTask>
        {
            new("Task 1", "Description 1", "Alice", null),
            new("Task 2", "Description 2", "Bob", new DateTime(2026, 6, 15))
        };

        var request = new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = recordingId,
            Name = recordingName,
            PlaudCreatedAt = plaudCreatedAt
        };

        _mockRepository
            .Setup(r => r.ExistsByPlaudIdAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });

        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("Test Headline", summary));

        _mockExtractor
            .Setup(e => e.ExtractAsync(summary, transcript, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractedTasks);

        _mockRepository
            .Setup(r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Skipped.Should().BeFalse();
        response.TranscriptId.Should().NotBeNull();

        _mockRepository.Verify(
            r => r.AddAsync(
                It.Is<MeetingTranscript>(t =>
                    t.PlaudRecordingId == recordingId &&
                    t.Status == MeetingTranscriptStatus.PendingReview &&
                    t.Tasks.Count == 2 &&
                    t.Tasks.All(task => task.MeetingTranscriptId == t.Id && task.Status == ProposedTaskStatus.Pending && !task.IsManuallyAdded)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockRepository.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenRecordingAlreadyIngested_SkipsWithoutSaving()
    {
        // Arrange
        var recordingId = "rec_002";

        var request = new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = recordingId,
            Name = "Duplicate Meeting",
            PlaudCreatedAt = DateTime.UtcNow
        };

        _mockRepository
            .Setup(r => r.ExistsByPlaudIdAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Skipped.Should().BeTrue();

        _mockRepository.Verify(
            r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _mockRepository.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _mockPlaudClient.Verify(
            c => c.GetTranscriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenExtractorReturnsEmptyList_SavesTranscriptWithZeroTasks()
    {
        // Arrange
        var recordingId = "rec_003";
        var recordingName = "Meeting With No Tasks";
        var plaudCreatedAt = DateTime.UtcNow;
        const string transcript = "transcript text";
        const string summary = "summary text";

        var request = new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = recordingId,
            Name = recordingName,
            PlaudCreatedAt = plaudCreatedAt
        };

        _mockRepository
            .Setup(r => r.ExistsByPlaudIdAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });

        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("Test Headline", summary));

        _mockExtractor
            .Setup(e => e.ExtractAsync(summary, transcript, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>());

        _mockRepository
            .Setup(r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();

        _mockRepository.Verify(
            r => r.AddAsync(
                It.Is<MeetingTranscript>(t => t.Tasks.Count == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenLlmReturnsNameWithoutEmail_FillsEmailFromDirectory()
    {
        // Arrange
        var request = new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = "rec_safety",
            Name = "Meeting",
            PlaudCreatedAt = DateTime.UtcNow
        };

        _mockRepository
            .Setup(r => r.ExistsByPlaudIdAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("transcript");
        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("Headline", "summary"));
        _mockExtractor
            .Setup(e => e.ExtractAsync("summary", "transcript", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>
            {
                new("Task", "Desc", "Andy", null, AssigneeEmail: null)
            });
        _mockDirectory
            .Setup(d => d.Resolve("Andy"))
            .Returns(new MeetingUser("andrea@anela.cz", "Andrea Nováková", new[] { "Andy" }));

        MeetingTranscript? saved = null;
        _mockRepository
            .Setup(r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .Callback<MeetingTranscript, CancellationToken>((t, _) => saved = t);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        saved.Should().NotBeNull();
        saved!.Tasks.Single().AssigneeEmail.Should().Be("andrea@anela.cz");
    }

    [Fact]
    public async Task Handle_WhenRecordingNotGenerated_SkipsWithoutSavingOrFetchingTranscript()
    {
        // Arrange
        var recordingId = "rec_raw";

        var request = new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = recordingId,
            Name = "Raw Recording",
            PlaudCreatedAt = DateTime.UtcNow
        };

        _mockRepository
            .Setup(r => r.ExistsByPlaudIdAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = false, SummaryAvailable = false, AudioAvailable = false });

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Skipped.Should().BeTrue();
        response.NotGenerated.Should().BeTrue();

        _mockPlaudClient.Verify(
            c => c.GetTranscriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _mockRepository.Verify(
            r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenRecordingNamePresent_RecordingNameWinsOverHeadline()
    {
        // Arrange
        var request = new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = "rec_named",
            Name = "Týmová porada: Z-boxy a dopravci",
            PlaudCreatedAt = DateTime.UtcNow
        };

        _mockRepository
            .Setup(r => r.ExistsByPlaudIdAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("transcript");
        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("2026-05-18 10:25:12", "summary"));
        _mockExtractor
            .Setup(e => e.ExtractAsync("summary", "transcript", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>());

        MeetingTranscript? saved = null;
        _mockRepository
            .Setup(r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .Callback<MeetingTranscript, CancellationToken>((t, _) => saved = t);
        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.Subject.Should().Be("Týmová porada: Z-boxy a dopravci");
    }

    [Fact]
    public async Task Handle_WhenRecordingNameEmpty_UsesHeadlineAsSubject()
    {
        // Arrange
        var request = new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = "rec_unnamed",
            Name = "",
            PlaudCreatedAt = DateTime.UtcNow
        };

        _mockRepository
            .Setup(r => r.ExistsByPlaudIdAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("transcript");
        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("Generated Meeting Title", "summary"));
        _mockExtractor
            .Setup(e => e.ExtractAsync("summary", "transcript", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>());

        MeetingTranscript? saved = null;
        _mockRepository
            .Setup(r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .Callback<MeetingTranscript, CancellationToken>((t, _) => saved = t);
        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.Subject.Should().Be("Generated Meeting Title");
    }

    [Fact]
    public async Task Handle_WhenRecordingNameWhitespace_UsesHeadlineAsSubject()
    {
        // Arrange
        var request = new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = "rec_whitespace",
            Name = "   ",
            PlaudCreatedAt = DateTime.UtcNow
        };

        _mockRepository
            .Setup(r => r.ExistsByPlaudIdAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("transcript");
        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("Generated Meeting Title", "summary"));
        _mockExtractor
            .Setup(e => e.ExtractAsync("summary", "transcript", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>());

        MeetingTranscript? saved = null;
        _mockRepository
            .Setup(r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .Callback<MeetingTranscript, CancellationToken>((t, _) => saved = t);
        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.Subject.Should().Be("Generated Meeting Title");
    }
}
