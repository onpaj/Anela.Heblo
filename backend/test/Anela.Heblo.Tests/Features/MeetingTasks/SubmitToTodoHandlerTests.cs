using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class SubmitToTodoHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repo = new();
    private readonly Mock<IGraphTodoService> _graph = new();

    private SubmitToTodoHandler CreateHandler() => new(
        _repo.Object,
        _graph.Object,
        NullLogger<SubmitToTodoHandler>.Instance);

    private static MeetingTranscript NewTranscript(params ProposedTask[] tasks)
    {
        return new MeetingTranscript
        {
            Id = Guid.NewGuid(),
            PlaudRecordingId = "rec-1",
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "Test meeting",
            Summary = "",
            RawTranscript = "",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = tasks.ToList()
        };
    }

    private static ProposedTask NewTask(
        ProposedTaskStatus status,
        string assignee = "Ondra Pajgrt",
        string? assigneeEmail = "ondra@anela.cz",
        string? externalId = null,
        string title = "Do thing")
    {
        return new ProposedTask
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = "desc",
            Assignee = assignee,
            AssigneeEmail = assigneeEmail,
            Status = status,
            ExternalTaskId = externalId,
            IsManuallyAdded = false
        };
    }

    private static MeetingTranscript BuildTranscriptWithApprovedTask(string? assigneeEmail)
    {
        var id = Guid.NewGuid();
        return new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec",
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "S", Summary = "Sum", RawTranscript = "raw",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = new List<ProposedTask>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    MeetingTranscriptId = id,
                    Title = "Task", Description = "Desc",
                    Assignee = "Andrea Nováková",
                    AssigneeEmail = assigneeEmail,
                    Status = ProposedTaskStatus.Approved,
                    ExternalTaskId = null
                }
            }
        };
    }

    [Fact]
    public async Task Handle_TranscriptNotFound_ReturnsResourceNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);
        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = Guid.NewGuid() }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _graph.Verify(g => g.ResolveUserIdByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AllTasksApprovedAndSubmitSucceed_TranscriptApproved()
    {
        var t1 = NewTask(ProposedTaskStatus.Approved, assignee: "A", assigneeEmail: "a@anela.cz", title: "T1");
        var t2 = NewTask(ProposedTaskStatus.Approved, assignee: "B", assigneeEmail: "b@anela.cz", title: "T2");
        var transcript = NewTranscript(t1, t2);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdByEmailAsync("a@anela.cz", It.IsAny<CancellationToken>())).ReturnsAsync("user-a");
        _graph.Setup(g => g.ResolveUserIdByEmailAsync("b@anela.cz", It.IsAny<CancellationToken>())).ReturnsAsync("user-b");
        _graph.Setup(g => g.CreateTodoTaskAsync("user-a", "T1", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-1", null));
        _graph.Setup(g => g.CreateTodoTaskAsync("user-b", "T2", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-2", null));

        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.SuccessCount.Should().Be(2);
        result.FailedCount.Should().Be(0);
        result.Errors.Should().BeEmpty();

        t1.ExternalTaskId.Should().Be("ext-1");
        t2.ExternalTaskId.Should().Be("ext-2");
        transcript.Status.Should().Be(MeetingTranscriptStatus.Approved);
        transcript.ReviewedAt.Should().NotBeNull();

        // Per-task save (2 tasks) + final save = 3 saves total.
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Handle_MixedApprovedAndRejected_ResultsInPartiallyApproved()
    {
        var approved = NewTask(ProposedTaskStatus.Approved, assigneeEmail: "approved@anela.cz", title: "OK");
        var rejected = NewTask(ProposedTaskStatus.Rejected, title: "NO");
        var transcript = NewTranscript(approved, rejected);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdByEmailAsync(approved.AssigneeEmail!, It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _graph.Setup(g => g.CreateTodoTaskAsync("u", "OK", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-ok", null));

        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        transcript.Status.Should().Be(MeetingTranscriptStatus.PartiallyApproved);

        _graph.Verify(g => g.CreateTodoTaskAsync(It.IsAny<string>(), "NO", It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PendingTask_StaysPendingReview()
    {
        var approved = NewTask(ProposedTaskStatus.Approved, assigneeEmail: "approved@anela.cz", title: "Yes");
        var pending = NewTask(ProposedTaskStatus.Pending, title: "Maybe");
        var transcript = NewTranscript(approved, pending);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdByEmailAsync(approved.AssigneeEmail!, It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _graph.Setup(g => g.CreateTodoTaskAsync("u", "Yes", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-yes", null));

        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.SuccessCount.Should().Be(1);
        transcript.Status.Should().Be(MeetingTranscriptStatus.PendingReview);
    }

    [Fact]
    public async Task Handle_AlreadySubmittedTask_IsSkipped()
    {
        var alreadyDone = NewTask(ProposedTaskStatus.Approved, assigneeEmail: "done@anela.cz", externalId: "ext-old", title: "Old");
        var newOne = NewTask(ProposedTaskStatus.Approved, assigneeEmail: "new@anela.cz", title: "New");
        var transcript = NewTranscript(alreadyDone, newOne);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdByEmailAsync(newOne.AssigneeEmail!, It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _graph.Setup(g => g.CreateTodoTaskAsync("u", "New", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-new", null));

        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.SuccessCount.Should().Be(1);
        _graph.Verify(g => g.CreateTodoTaskAsync(It.IsAny<string>(), "Old", It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
        transcript.Status.Should().Be(MeetingTranscriptStatus.Approved);
    }

    [Fact]
    public async Task Handle_AssigneeEmailNotResolved_CountsAsFailure()
    {
        var task = NewTask(ProposedTaskStatus.Approved, assignee: "Ghost", assigneeEmail: "ghost@anela.cz", title: "Spook");
        var transcript = NewTranscript(task);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdByEmailAsync("ghost@anela.cz", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("ghost@anela.cz").And.Contain("Spook");
        task.ExternalTaskId.Should().BeNull();
        transcript.Status.Should().Be(MeetingTranscriptStatus.PendingReview);

        _graph.Verify(g => g.CreateTodoTaskAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_GraphCreateFails_RecordsErrorAndContinues()
    {
        var bad = NewTask(ProposedTaskStatus.Approved, assignee: "A", assigneeEmail: "a@anela.cz", title: "Bad");
        var good = NewTask(ProposedTaskStatus.Approved, assignee: "B", assigneeEmail: "b@anela.cz", title: "Good");
        var transcript = NewTranscript(bad, good);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdByEmailAsync("a@anela.cz", It.IsAny<CancellationToken>())).ReturnsAsync("user-a");
        _graph.Setup(g => g.ResolveUserIdByEmailAsync("b@anela.cz", It.IsAny<CancellationToken>())).ReturnsAsync("user-b");
        _graph.Setup(g => g.CreateTodoTaskAsync("user-a", "Bad", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(false, null, "Graph 500"));
        _graph.Setup(g => g.CreateTodoTaskAsync("user-b", "Good", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-good", null));

        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Bad");

        bad.ExternalTaskId.Should().BeNull();
        good.ExternalTaskId.Should().Be("ext-good");

        transcript.Status.Should().Be(MeetingTranscriptStatus.PendingReview);
    }

    [Fact]
    public async Task Handle_PersistsExternalTaskIdImmediatelyAfterEachSuccess()
    {
        var t1 = NewTask(ProposedTaskStatus.Approved, assignee: "A", assigneeEmail: "a@anela.cz", title: "T1");
        var t2 = NewTask(ProposedTaskStatus.Approved, assignee: "B", assigneeEmail: "b@anela.cz", title: "T2");
        var transcript = NewTranscript(t1, t2);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _graph.Setup(g => g.CreateTodoTaskAsync("u", "T1", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-1", null));
        _graph.Setup(g => g.CreateTodoTaskAsync("u", "T2", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-2", null));

        var saveCount = 0;
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() =>
            {
                saveCount++;
                if (saveCount == 1) t1.ExternalTaskId.Should().Be("ext-1");
                if (saveCount == 2) t2.ExternalTaskId.Should().Be("ext-2");
                if (saveCount == 3)
                {
                    transcript.Status.Should().Be(MeetingTranscriptStatus.Approved);
                    transcript.ReviewedAt.Should().NotBeNull();
                }
            });

        var handler = CreateHandler();

        await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Handle_ReRunSafelySkipsAlreadyProcessedTasks()
    {
        var t1 = NewTask(ProposedTaskStatus.Approved, assigneeEmail: "done@anela.cz", externalId: "ext-1", title: "Done");
        var t2 = NewTask(ProposedTaskStatus.Approved, assigneeEmail: "todo@anela.cz", title: "Todo");
        var transcript = NewTranscript(t1, t2);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdByEmailAsync(t2.AssigneeEmail!, It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _graph.Setup(g => g.CreateTodoTaskAsync("u", "Todo", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-2", null));

        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.SuccessCount.Should().Be(1);
        _graph.Verify(g => g.CreateTodoTaskAsync(It.IsAny<string>(), "Done", It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SkipsAndReportsTaskWithNoAssigneeEmail()
    {
        // Arrange — transcript with one approved task that has AssigneeEmail = null
        var transcript = BuildTranscriptWithApprovedTask(assigneeEmail: null);
        _repo
            .Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(
            new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        // Assert
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("no resolved user");
        _graph.Verify(
            s => s.ResolveUserIdByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SubmitsTaskWithResolvedAssigneeEmail()
    {
        // Arrange
        var transcript = BuildTranscriptWithApprovedTask(assigneeEmail: "andrea@anela.cz");
        _repo
            .Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        _graph
            .Setup(s => s.ResolveUserIdByEmailAsync("andrea@anela.cz", It.IsAny<CancellationToken>()))
            .ReturnsAsync("graph-user-id");
        _graph
            .Setup(s => s.CreateTodoTaskAsync(
                "graph-user-id", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-1", null));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(
            new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        // Assert
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
    }
}
