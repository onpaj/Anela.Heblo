# Subtask 4: Write Handlers — Edit, Approve/Reject, Add Task

**Parent Epic:** Meeting Task Validation Checkpoint

CRITICAL - This is part of epic, you **MUST** use epic branch - feat/meeting-task-validation-epic as a source for this feature branch and create a PR back to this branch instead of main


## Task 6: UpdateProposedTask, UpdateProposedTaskStatus, AddProposedTask Handlers

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskHandler.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusHandler.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs`

- [ ] **Step 1: Create UpdateProposedTask request and handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskRequest.cs
using Anela.Heblo.Application.Shared;
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;

public class UpdateProposedTaskRequest : IRequest<BaseResponse>
{
    public Guid TranscriptId { get; set; }
    public Guid TaskId { get; set; }

    [Required]
    public string Title { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    [Required]
    public string Assignee { get; set; } = null!;

    public DateTime? DueDate { get; set; }
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskHandler.cs
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;

public class UpdateProposedTaskHandler : IRequestHandler<UpdateProposedTaskRequest, BaseResponse>
{
    private readonly IMeetingTranscriptRepository _repository;

    public UpdateProposedTaskHandler(IMeetingTranscriptRepository repository)
    {
        _repository = repository;
    }

    public async Task<BaseResponse> Handle(UpdateProposedTaskRequest request, CancellationToken cancellationToken)
    {
        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript == null)
            return new UpdateProposedTaskResponse { Success = false, ErrorCode = ErrorCodes.NotFound };

        var task = transcript.Tasks.FirstOrDefault(t => t.Id == request.TaskId);
        if (task == null)
            return new UpdateProposedTaskResponse { Success = false, ErrorCode = ErrorCodes.NotFound };

        task.Title = request.Title;
        task.Description = request.Description;
        task.Assignee = request.Assignee;
        task.DueDate = request.DueDate;

        await _repository.SaveChangesAsync(cancellationToken);
        return new UpdateProposedTaskResponse();
    }
}

public class UpdateProposedTaskResponse : BaseResponse { }
```

- [ ] **Step 2: Create UpdateProposedTaskStatus request and handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusRequest.cs
using Anela.Heblo.Application.Shared;
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;

public class UpdateProposedTaskStatusRequest : IRequest<BaseResponse>
{
    public Guid TranscriptId { get; set; }
    public Guid TaskId { get; set; }

    [Required]
    public string Status { get; set; } = null!;
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusHandler.cs
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;

public class UpdateProposedTaskStatusHandler : IRequestHandler<UpdateProposedTaskStatusRequest, BaseResponse>
{
    private readonly IMeetingTranscriptRepository _repository;

    public UpdateProposedTaskStatusHandler(IMeetingTranscriptRepository repository)
    {
        _repository = repository;
    }

    public async Task<BaseResponse> Handle(UpdateProposedTaskStatusRequest request, CancellationToken cancellationToken)
    {
        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript == null)
            return new UpdateProposedTaskStatusResponse { Success = false, ErrorCode = ErrorCodes.NotFound };

        var task = transcript.Tasks.FirstOrDefault(t => t.Id == request.TaskId);
        if (task == null)
            return new UpdateProposedTaskStatusResponse { Success = false, ErrorCode = ErrorCodes.NotFound };

        if (!Enum.TryParse<ProposedTaskStatus>(request.Status, true, out var newStatus))
            return new UpdateProposedTaskStatusResponse { Success = false, ErrorCode = ErrorCodes.ValidationError };

        task.Status = newStatus;
        await _repository.SaveChangesAsync(cancellationToken);
        return new UpdateProposedTaskStatusResponse();
    }
}

public class UpdateProposedTaskStatusResponse : BaseResponse { }
```

- [ ] **Step 3: Create AddProposedTask request and handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskRequest.cs
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Shared;
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;

public class AddProposedTaskRequest : IRequest<AddProposedTaskResponse>
{
    public Guid TranscriptId { get; set; }

    [Required]
    public string Title { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    [Required]
    public string Assignee { get; set; } = null!;

    public DateTime? DueDate { get; set; }
}

public class AddProposedTaskResponse : BaseResponse
{
    public ProposedTaskDto Task { get; set; } = null!;
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskHandler.cs
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;

public class AddProposedTaskHandler : IRequestHandler<AddProposedTaskRequest, AddProposedTaskResponse>
{
    private readonly IMeetingTranscriptRepository _repository;

    public AddProposedTaskHandler(IMeetingTranscriptRepository repository)
    {
        _repository = repository;
    }

    public async Task<AddProposedTaskResponse> Handle(AddProposedTaskRequest request, CancellationToken cancellationToken)
    {
        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript == null)
            return new AddProposedTaskResponse { Success = false, ErrorCode = Application.Shared.ErrorCodes.NotFound };

        var task = new ProposedTask
        {
            Id = Guid.NewGuid(),
            MeetingTranscriptId = transcript.Id,
            Title = request.Title,
            Description = request.Description,
            Assignee = request.Assignee,
            DueDate = request.DueDate,
            Status = ProposedTaskStatus.Pending,
            IsManuallyAdded = true
        };

        transcript.Tasks.Add(task);
        await _repository.SaveChangesAsync(cancellationToken);

        return new AddProposedTaskResponse
        {
            Task = new ProposedTaskDto
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                Assignee = task.Assignee,
                DueDate = task.DueDate,
                Status = task.Status.ToString(),
                IsManuallyAdded = true
            }
        };
    }
}
```

- [ ] **Step 4: Write tests**

```csharp
// backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class UpdateProposedTaskHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repoMock = new();

    private MeetingTranscript CreateTranscriptWithTask(out Guid transcriptId, out Guid taskId)
    {
        transcriptId = Guid.NewGuid();
        taskId = Guid.NewGuid();
        return new MeetingTranscript
        {
            Id = transcriptId,
            Subject = "Test",
            Summary = "Summary",
            SourceEmail = "test@example.com",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = new List<ProposedTask>
            {
                new() { Id = taskId, Title = "Original", Description = "Desc", Assignee = "Alice", Status = ProposedTaskStatus.Pending }
            }
        };
    }

    [Fact]
    public async Task UpdateTask_ModifiesFields()
    {
        var transcript = CreateTranscriptWithTask(out var tId, out var taskId);
        _repoMock.Setup(r => r.GetByIdAsync(tId, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);

        var handler = new UpdateProposedTaskHandler(_repoMock.Object);
        var result = await handler.Handle(new UpdateProposedTaskRequest
        {
            TranscriptId = tId, TaskId = taskId, Title = "Updated", Description = "New desc", Assignee = "Bob"
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Updated", transcript.Tasks[0].Title);
        Assert.Equal("Bob", transcript.Tasks[0].Assignee);
    }

    [Fact]
    public async Task UpdateTaskStatus_ApprovesTask()
    {
        var transcript = CreateTranscriptWithTask(out var tId, out var taskId);
        _repoMock.Setup(r => r.GetByIdAsync(tId, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);

        var handler = new UpdateProposedTaskStatusHandler(_repoMock.Object);
        var result = await handler.Handle(new UpdateProposedTaskStatusRequest
        {
            TranscriptId = tId, TaskId = taskId, Status = "Approved"
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(ProposedTaskStatus.Approved, transcript.Tasks[0].Status);
    }

    [Fact]
    public async Task AddTask_CreatesManualTask()
    {
        var transcript = CreateTranscriptWithTask(out var tId, out _);
        _repoMock.Setup(r => r.GetByIdAsync(tId, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);

        var handler = new AddProposedTaskHandler(_repoMock.Object);
        var result = await handler.Handle(new AddProposedTaskRequest
        {
            TranscriptId = tId, Title = "New Task", Assignee = "Charlie", Description = "Manual"
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Task.IsManuallyAdded);
        Assert.Equal(2, transcript.Tasks.Count);
    }
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~UpdateProposedTaskHandlerTests"`
Expected: All 3 tests PASS

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/
git add backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs
git commit -m "feat(meeting-tasks): add UpdateProposedTask, UpdateProposedTaskStatus, AddProposedTask handlers with tests"
```

---


---

> **Integration:** Create your feature branch from `feat/meeting-task-validation-epic`. When done, open a PR targeting `feat/meeting-task-validation-epic` (not `main`).