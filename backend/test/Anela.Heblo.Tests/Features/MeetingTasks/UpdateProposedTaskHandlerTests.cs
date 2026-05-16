using Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class UpdateProposedTaskHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repositoryMock = new();

    private MeetingTranscript CreateTranscriptWithTask(out Guid transcriptId, out Guid taskId)
    {
        transcriptId = Guid.NewGuid();
        taskId = Guid.NewGuid();
        var transcript = new MeetingTranscript
        {
            Id = transcriptId,
            PlaudRecordingId = "rec-test",
            PlaudCreatedAt = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc),
            Subject = "Sprint Planning",
            Summary = "Plan the sprint",
            RawTranscript = "",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = new DateTime(2026, 5, 1, 9, 30, 0, DateTimeKind.Utc),
            Tasks = new List<ProposedTask>
            {
                new ProposedTask
                {
                    Id = taskId,
                    MeetingTranscriptId = transcriptId,
                    Title = "Original title",
                    Description = "Original description",
                    Assignee = "alice",
                    DueDate = null,
                    Status = ProposedTaskStatus.Pending,
                    IsManuallyAdded = false
                }
            }
        };
        return transcript;
    }

    [Fact]
    public async Task UpdateTask_ModifiesFields()
    {
        // Arrange
        var transcript = CreateTranscriptWithTask(out var transcriptId, out var taskId);
        _repositoryMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        var handler = new UpdateProposedTaskHandler(
            _repositoryMock.Object,
            NullLogger<UpdateProposedTaskHandler>.Instance);
        var request = new UpdateProposedTaskRequest
        {
            TranscriptId = transcriptId,
            TaskId = taskId,
            Title = "New title",
            Description = "New description",
            Assignee = "bob",
            DueDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        var task = transcript.Tasks.Single();
        task.Title.Should().Be("New title");
        task.Description.Should().Be("New description");
        task.Assignee.Should().Be("bob");
        task.DueDate.Should().Be(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateTask_MapsAssigneeEmail()
    {
        // Arrange
        var transcript = CreateTranscriptWithTask(out var transcriptId, out var taskId);
        _repositoryMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        var handler = new UpdateProposedTaskHandler(
            _repositoryMock.Object,
            NullLogger<UpdateProposedTaskHandler>.Instance);
        var request = new UpdateProposedTaskRequest
        {
            TranscriptId = transcriptId,
            TaskId = taskId,
            Title = "Title",
            Description = "Description",
            Assignee = "alice",
            AssigneeEmail = "andrea@anela.cz",
            DueDate = null
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        transcript.Tasks.Single().AssigneeEmail.Should().Be("andrea@anela.cz");
    }

    [Fact]
    public async Task UpdateTask_ReturnsNotFound_WhenTranscriptMissing()
    {
        // Arrange
        var transcriptId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);
        var handler = new UpdateProposedTaskHandler(
            _repositoryMock.Object,
            NullLogger<UpdateProposedTaskHandler>.Instance);
        var request = new UpdateProposedTaskRequest
        {
            TranscriptId = transcriptId,
            TaskId = Guid.NewGuid(),
            Title = "Some title",
            Description = "Some description",
            Assignee = "alice",
            DueDate = null
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateTask_ReturnsNotFound_WhenTaskMissing()
    {
        // Arrange
        var transcript = CreateTranscriptWithTask(out var transcriptId, out _);
        _repositoryMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        var handler = new UpdateProposedTaskHandler(
            _repositoryMock.Object,
            NullLogger<UpdateProposedTaskHandler>.Instance);
        var request = new UpdateProposedTaskRequest
        {
            TranscriptId = transcriptId,
            TaskId = Guid.NewGuid(), // non-existent task ID
            Title = "Some title",
            Description = "Some description",
            Assignee = "alice",
            DueDate = null
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateTaskStatus_ApprovesTask()
    {
        // Arrange
        var transcript = CreateTranscriptWithTask(out var transcriptId, out var taskId);
        _repositoryMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        var handler = new UpdateProposedTaskStatusHandler(
            _repositoryMock.Object,
            NullLogger<UpdateProposedTaskStatusHandler>.Instance);
        var request = new UpdateProposedTaskStatusRequest
        {
            TranscriptId = transcriptId,
            TaskId = taskId,
            Status = "Approved"
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        transcript.Tasks.Single().Status.Should().Be(ProposedTaskStatus.Approved);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateTaskStatus_ReturnsNotFound_WhenTranscriptMissing()
    {
        // Arrange
        var transcriptId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);
        var handler = new UpdateProposedTaskStatusHandler(
            _repositoryMock.Object,
            NullLogger<UpdateProposedTaskStatusHandler>.Instance);
        var request = new UpdateProposedTaskStatusRequest
        {
            TranscriptId = transcriptId,
            TaskId = Guid.NewGuid(),
            Status = "Approved"
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateTaskStatus_ReturnsNotFound_WhenTaskMissing()
    {
        // Arrange
        var transcript = CreateTranscriptWithTask(out var transcriptId, out _);
        _repositoryMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        var handler = new UpdateProposedTaskStatusHandler(
            _repositoryMock.Object,
            NullLogger<UpdateProposedTaskStatusHandler>.Instance);
        var request = new UpdateProposedTaskStatusRequest
        {
            TranscriptId = transcriptId,
            TaskId = Guid.NewGuid(), // non-existent task ID
            Status = "Approved"
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateTaskStatus_ReturnsValidationError_WhenStatusInvalid()
    {
        // Arrange
        var transcript = CreateTranscriptWithTask(out var transcriptId, out var taskId);
        _repositoryMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        var handler = new UpdateProposedTaskStatusHandler(
            _repositoryMock.Object,
            NullLogger<UpdateProposedTaskStatusHandler>.Instance);
        var request = new UpdateProposedTaskStatusRequest
        {
            TranscriptId = transcriptId,
            TaskId = taskId,
            Status = "InvalidStatusXYZ"
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddTask_CreatesManualTask()
    {
        // Arrange
        var transcript = CreateTranscriptWithTask(out var transcriptId, out _);
        _repositoryMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        var handler = new AddProposedTaskHandler(
            _repositoryMock.Object,
            NullLogger<AddProposedTaskHandler>.Instance);
        var dueDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var request = new AddProposedTaskRequest
        {
            TranscriptId = transcriptId,
            Title = "Manually added task",
            Description = "Added by reviewer",
            Assignee = "carol",
            DueDate = dueDate
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Task.Should().NotBeNull();
        result.Task.Title.Should().Be("Manually added task");
        result.Task.Description.Should().Be("Added by reviewer");
        result.Task.Assignee.Should().Be("carol");
        result.Task.DueDate.Should().Be(dueDate);
        result.Task.Status.Should().Be("Pending");
        result.Task.IsManuallyAdded.Should().BeTrue();
        result.Task.ExternalTaskId.Should().BeNull();

        transcript.Tasks.Should().HaveCount(2);
        var addedEntity = transcript.Tasks.Last();
        addedEntity.Title.Should().Be("Manually added task");
        addedEntity.IsManuallyAdded.Should().BeTrue();
        addedEntity.Status.Should().Be(ProposedTaskStatus.Pending);
        addedEntity.MeetingTranscriptId.Should().Be(transcriptId);
        result.Task.Id.Should().Be(addedEntity.Id);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddTask_ReturnsNotFound_WhenTranscriptMissing()
    {
        // Arrange
        var transcriptId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);
        var handler = new AddProposedTaskHandler(
            _repositoryMock.Object,
            NullLogger<AddProposedTaskHandler>.Instance);
        var request = new AddProposedTaskRequest
        {
            TranscriptId = transcriptId,
            Title = "Some task",
            Description = "Some description",
            Assignee = "alice",
            DueDate = null
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        result.Task.Should().BeNull();
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
