# Meeting Task Write Handlers — Edit, Approve/Reject, Add Task — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three MediatR write-side handlers — `UpdateProposedTask`, `UpdateProposedTaskStatus`, `AddProposedTask` — to the Meeting Tasks vertical slice so users can edit, approve/reject, and manually add tasks on a `MeetingTranscript` aggregate.

**Architecture:** Three independent UseCase folders under `Application/Features/MeetingTasks/UseCases/`, each containing a request/response/handler triple following the sibling `GetTranscriptDetail` convention. Handlers load the aggregate via `IMeetingTranscriptRepository.GetByIdAsync`, mutate via EF Core change tracking, persist via `SaveChangesAsync`, and return a `BaseResponse`-derived response carrying `ErrorCodes.ResourceNotFound` / `ErrorCodes.ValidationError` for known failures. No DI changes (MediatR scan picks up handlers), no schema changes, no controller wiring (out of scope).

**Tech Stack:** .NET 8, C#, MediatR, EF Core (via existing repository), xUnit + Moq + FluentAssertions + `NullLogger<T>` for tests.

---

## Prerequisites

This worktree branch (`feat-meeting-tasks-write-handlers-edit-approv`) was created from `main`. The epic branch `feat/meeting-task-validation-epic` contains the entire prerequisite chain (domain entities, enum, repository interface + implementation, `ProposedTaskDto`, sibling handlers, EF migration). **None of those files exist on disk in this worktree.** Task 0 below rebases this branch onto the epic so that all referenced types resolve. Do not skip it — every subsequent task will fail to compile otherwise.

The PR target for this work is `feat/meeting-task-validation-epic`, not `main`.

---

## File Structure

### Files to create

```
backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/
├── UpdateProposedTask/
│   ├── UpdateProposedTaskRequest.cs    (request DTO, IRequest<UpdateProposedTaskResponse>)
│   ├── UpdateProposedTaskResponse.cs   (response, BaseResponse-derived)
│   └── UpdateProposedTaskHandler.cs    (handler with logger + repo)
├── UpdateProposedTaskStatus/
│   ├── UpdateProposedTaskStatusRequest.cs
│   ├── UpdateProposedTaskStatusResponse.cs
│   └── UpdateProposedTaskStatusHandler.cs
└── AddProposedTask/
    ├── AddProposedTaskRequest.cs
    ├── AddProposedTaskResponse.cs       (carries ProposedTaskDto Task)
    └── AddProposedTaskHandler.cs

backend/test/Anela.Heblo.Tests/Features/MeetingTasks/
└── UpdateProposedTaskHandlerTests.cs    (all 3 happy-path tests + helper)
```

### Files to modify

None. MediatR assembly scan in the existing `AddMeetingTasksModule` registration auto-picks the new handlers; no DI changes are needed.

### Files referenced (must already exist on epic branch after Task 0)

- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs`
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTask.cs`
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTaskStatus.cs`
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscriptStatus.cs`
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/ProposedTaskDto.cs`
- `backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs`
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`

---

## Working Tree Reference

For convenience, here are the exact shapes of the types this plan depends on (taken verbatim from `origin/feat/meeting-task-validation-epic`):

```csharp
// Domain.Features.MeetingTasks.MeetingTranscript
public class MeetingTranscript
{
    public Guid Id { get; set; }
    public string PlaudRecordingId { get; set; } = null!;
    public DateTime PlaudCreatedAt { get; set; }
    public string Subject { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string RawTranscript { get; set; } = null!;
    public MeetingTranscriptStatus Status { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUser { get; set; }
    public List<ProposedTask> Tasks { get; set; } = new();
}

// Domain.Features.MeetingTasks.ProposedTask
public class ProposedTask
{
    public Guid Id { get; set; }
    public Guid MeetingTranscriptId { get; set; }
    public MeetingTranscript MeetingTranscript { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Assignee { get; set; } = null!;
    public DateTime? DueDate { get; set; }
    public ProposedTaskStatus Status { get; set; }
    public string? ExternalTaskId { get; set; }
    public bool IsManuallyAdded { get; set; }
}

// Domain.Features.MeetingTasks.ProposedTaskStatus
public enum ProposedTaskStatus { Pending = 1, Approved = 2, Rejected = 3 }

// Domain.Features.MeetingTasks.MeetingTranscriptStatus
public enum MeetingTranscriptStatus { PendingReview = 1, Approved = 2, PartiallyApproved = 3 }

// Domain.Features.MeetingTasks.IMeetingTranscriptRepository
public interface IMeetingTranscriptRepository
{
    Task<MeetingTranscript?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(
        MeetingTranscriptStatus? statusFilter, int page, int pageSize, CancellationToken ct = default);
    Task<bool> ExistsByPlaudIdAsync(string plaudRecordingId, CancellationToken ct = default);
    Task AddAsync(MeetingTranscript transcript, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

// Application.Features.MeetingTasks.Contracts.ProposedTaskDto
public class ProposedTaskDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Assignee { get; set; } = null!;
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = null!;
    public string? ExternalTaskId { get; set; }
    public bool IsManuallyAdded { get; set; }
}

// Application.Shared.ErrorCodes (relevant members only)
public enum ErrorCodes
{
    ValidationError = 0001,    // [HttpStatusCode(HttpStatusCode.BadRequest)]
    ResourceNotFound = 0006,   // [HttpStatusCode(HttpStatusCode.NotFound)]
    // ...
}

// Application.Shared.BaseResponse (relevant members only)
public abstract class BaseResponse
{
    public bool Success { get; set; } = true;
    public ErrorCodes? ErrorCode { get; set; }
    public Dictionary<string, string>? Params { get; set; }
    protected BaseResponse() { Success = true; }
    protected BaseResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
    {
        Success = false;
        ErrorCode = errorCode;
        Params = parameters;
    }
}
```

---

## Task 0: Rebase branch onto epic

**Files:**
- No source files. This task only changes the branch's git history.

- [ ] **Step 1: Fetch the epic branch from origin**

Run:

```bash
git fetch origin feat/meeting-task-validation-epic
```

Expected: command exits 0; `origin/feat/meeting-task-validation-epic` is now a known ref locally.

- [ ] **Step 2: Confirm the current branch is not already based on the epic**

Run:

```bash
git merge-base --is-ancestor origin/feat/meeting-task-validation-epic HEAD && echo OK || echo NEEDS_REBASE
```

Expected: `NEEDS_REBASE`. If `OK`, this task is already done — skip to Task 1.

- [ ] **Step 3: Confirm the current branch has no commits beyond pipeline artifacts**

Run:

```bash
git log --oneline main..HEAD
```

Expected output: only artifact-upload commits (e.g., `agent: upload artifacts/...spec.r1.md`, `agent: upload artifacts/...arch-review.r1.md`, `agent: upload artifacts/...brief.md`). No source-code commits. If there are any source-code commits, STOP and ask before continuing — rebase strategy depends on what they contain.

- [ ] **Step 4: Reset the branch onto the epic**

Run:

```bash
git reset --hard origin/feat/meeting-task-validation-epic
```

Expected: working tree now reflects the epic branch tip. The pipeline-uploaded artifacts under `artifacts/feat-meeting-tasks-write-handlers-edit-approv/` will be gone from the working tree, but they remain available in `origin/feat-meeting-tasks-write-handlers-edit-approv` (unmodified upstream) and the brief is still accessible in the agent's context — no further action needed.

- [ ] **Step 5: Verify the prerequisite files now exist**

Run:

```bash
ls backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs \
   backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTask.cs \
   backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs \
   backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/ProposedTaskDto.cs \
   backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailHandler.cs
```

Expected: all five files listed without errors.

- [ ] **Step 6: Build to confirm the baseline compiles**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Run existing MeetingTasks tests as a sanity check**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.MeetingTasks"
```

Expected: existing tests (`GetTranscriptDetailHandlerTests`, `GetTranscriptListHandlerTests`, `IngestPlaudRecordingHandlerTests`, etc.) all pass.

- [ ] **Step 8: No commit for this task**

The rebase itself is the commit boundary. Do not create a new commit. The next task's first commit will sit on top of the epic.

---

## Task 1: UpdateProposedTask — request, response, handler, test

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs`

- [ ] **Step 1: Write the failing test (creates the test file and helper)**

Create `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs` with this content:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;
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
}
```

> Note: This file references `AddProposedTask*` and `UpdateProposedTaskStatus*` types in its `using` block, which won't compile until Tasks 2 and 3 land. That is intentional — those two tests will be added in their respective tasks. For Task 1, comment out the two extra `using` lines that don't resolve yet **only if needed to compile the test for this task in isolation**. They will be re-added when Tasks 2 and 3 add their tests.

Decision: leave both extra `using` lines commented out for now to keep the test file compiling between tasks. Use:

```csharp
// using Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;
// using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;
```

The commented lines will be uncommented in Tasks 2 and 3.

- [ ] **Step 2: Run the test to verify it fails (compile error)**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdateProposedTaskHandlerTests"
```

Expected: build failure. Errors should reference unresolved `UpdateProposedTaskHandler`, `UpdateProposedTaskRequest`. That's the RED state.

- [ ] **Step 3: Create `UpdateProposedTaskRequest.cs`**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;

public class UpdateProposedTaskRequest : IRequest<UpdateProposedTaskResponse>
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

- [ ] **Step 4: Create `UpdateProposedTaskResponse.cs`**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;

public class UpdateProposedTaskResponse : BaseResponse
{
    public UpdateProposedTaskResponse() { }
    public UpdateProposedTaskResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 5: Create `UpdateProposedTaskHandler.cs`**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;

public class UpdateProposedTaskHandler : IRequestHandler<UpdateProposedTaskRequest, UpdateProposedTaskResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly ILogger<UpdateProposedTaskHandler> _logger;

    public UpdateProposedTaskHandler(
        IMeetingTranscriptRepository repository,
        ILogger<UpdateProposedTaskHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<UpdateProposedTaskResponse> Handle(UpdateProposedTaskRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Updating proposed task — TranscriptId: {TranscriptId}, TaskId: {TaskId}",
            request.TranscriptId, request.TaskId);

        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {TranscriptId} not found", request.TranscriptId);
            return new UpdateProposedTaskResponse(ErrorCodes.ResourceNotFound);
        }

        var task = transcript.Tasks.FirstOrDefault(t => t.Id == request.TaskId);
        if (task is null)
        {
            _logger.LogWarning(
                "Proposed task {TaskId} not found on transcript {TranscriptId}",
                request.TaskId, request.TranscriptId);
            return new UpdateProposedTaskResponse(ErrorCodes.ResourceNotFound);
        }

        task.Title = request.Title;
        task.Description = request.Description;
        task.Assignee = request.Assignee;
        task.DueDate = request.DueDate;

        await _repository.SaveChangesAsync(cancellationToken);
        return new UpdateProposedTaskResponse();
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdateProposedTaskHandlerTests.UpdateTask_ModifiesFields"
```

Expected: 1 test passed, 0 failed.

- [ ] **Step 7: Build the application project for full type-check**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 8: Run `dotnet format` on changed files**

Run:

```bash
dotnet format backend/Anela.Heblo.sln --include \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskRequest.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskResponse.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs
```

Expected: command exits 0; no changes required (or whitespace cleanups applied).

- [ ] **Step 9: Commit**

Run:

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs
git commit -m "feat: add UpdateProposedTask handler for editing meeting task fields"
```

---

## Task 2: UpdateProposedTaskStatus — request, response, handler, test

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs` (uncomment `using` line, add second test)

- [ ] **Step 1: Add the failing test**

Edit `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs`:

a) Uncomment the previously commented `using` line:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;
```

b) Append this test method inside the class, after `UpdateTask_ModifiesFields`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it fails (compile error)**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdateProposedTaskHandlerTests.UpdateTaskStatus_ApprovesTask"
```

Expected: build failure; errors reference `UpdateProposedTaskStatusHandler` and `UpdateProposedTaskStatusRequest`.

- [ ] **Step 3: Create `UpdateProposedTaskStatusRequest.cs`**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;

public class UpdateProposedTaskStatusRequest : IRequest<UpdateProposedTaskStatusResponse>
{
    public Guid TranscriptId { get; set; }
    public Guid TaskId { get; set; }

    [Required]
    public string Status { get; set; } = null!;
}
```

- [ ] **Step 4: Create `UpdateProposedTaskStatusResponse.cs`**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;

public class UpdateProposedTaskStatusResponse : BaseResponse
{
    public UpdateProposedTaskStatusResponse() { }
    public UpdateProposedTaskStatusResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 5: Create `UpdateProposedTaskStatusHandler.cs`**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;

public class UpdateProposedTaskStatusHandler : IRequestHandler<UpdateProposedTaskStatusRequest, UpdateProposedTaskStatusResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly ILogger<UpdateProposedTaskStatusHandler> _logger;

    public UpdateProposedTaskStatusHandler(
        IMeetingTranscriptRepository repository,
        ILogger<UpdateProposedTaskStatusHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<UpdateProposedTaskStatusResponse> Handle(UpdateProposedTaskStatusRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Updating proposed task status — TranscriptId: {TranscriptId}, TaskId: {TaskId}, Status: {Status}",
            request.TranscriptId, request.TaskId, request.Status);

        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {TranscriptId} not found", request.TranscriptId);
            return new UpdateProposedTaskStatusResponse(ErrorCodes.ResourceNotFound);
        }

        var task = transcript.Tasks.FirstOrDefault(t => t.Id == request.TaskId);
        if (task is null)
        {
            _logger.LogWarning(
                "Proposed task {TaskId} not found on transcript {TranscriptId}",
                request.TaskId, request.TranscriptId);
            return new UpdateProposedTaskStatusResponse(ErrorCodes.ResourceNotFound);
        }

        if (!Enum.TryParse<ProposedTaskStatus>(request.Status, ignoreCase: true, out var newStatus))
        {
            _logger.LogWarning("Invalid proposed task status value: {Status}", request.Status);
            return new UpdateProposedTaskStatusResponse(ErrorCodes.ValidationError);
        }

        task.Status = newStatus;

        await _repository.SaveChangesAsync(cancellationToken);
        return new UpdateProposedTaskStatusResponse();
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdateProposedTaskHandlerTests.UpdateTaskStatus_ApprovesTask"
```

Expected: 1 test passed, 0 failed.

- [ ] **Step 7: Build the application project**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 8: Run `dotnet format` on changed files**

Run:

```bash
dotnet format backend/Anela.Heblo.sln --include \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusRequest.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusResponse.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs
```

Expected: exits 0.

- [ ] **Step 9: Commit**

Run:

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs
git commit -m "feat: add UpdateProposedTaskStatus handler for task lifecycle transitions"
```

---

## Task 3: AddProposedTask — request, response, handler, test

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs` (uncomment `using` line, add third test)

- [ ] **Step 1: Add the failing test**

Edit `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs`:

a) Uncomment the previously commented `using` line:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;
```

b) Append this test method inside the class:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it fails (compile error)**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdateProposedTaskHandlerTests.AddTask_CreatesManualTask"
```

Expected: build failure; errors reference `AddProposedTaskHandler`, `AddProposedTaskRequest`.

- [ ] **Step 3: Create `AddProposedTaskRequest.cs`**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskRequest.cs`:

```csharp
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
```

- [ ] **Step 4: Create `AddProposedTaskResponse.cs`**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;

public class AddProposedTaskResponse : BaseResponse
{
    public AddProposedTaskResponse() { }
    public AddProposedTaskResponse(ErrorCodes errorCode) : base(errorCode) { }

    public ProposedTaskDto Task { get; set; } = null!;
}
```

- [ ] **Step 5: Create `AddProposedTaskHandler.cs`**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;

public class AddProposedTaskHandler : IRequestHandler<AddProposedTaskRequest, AddProposedTaskResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly ILogger<AddProposedTaskHandler> _logger;

    public AddProposedTaskHandler(
        IMeetingTranscriptRepository repository,
        ILogger<AddProposedTaskHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<AddProposedTaskResponse> Handle(AddProposedTaskRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Adding manual proposed task — TranscriptId: {TranscriptId}, Title: {Title}",
            request.TranscriptId, request.Title);

        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {TranscriptId} not found", request.TranscriptId);
            return new AddProposedTaskResponse(ErrorCodes.ResourceNotFound);
        }

        var task = new ProposedTask
        {
            Id = Guid.NewGuid(),
            MeetingTranscriptId = transcript.Id,
            Title = request.Title,
            Description = request.Description,
            Assignee = request.Assignee,
            DueDate = request.DueDate,
            Status = ProposedTaskStatus.Pending,
            ExternalTaskId = null,
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
                ExternalTaskId = task.ExternalTaskId,
                IsManuallyAdded = task.IsManuallyAdded
            }
        };
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdateProposedTaskHandlerTests.AddTask_CreatesManualTask"
```

Expected: 1 test passed, 0 failed.

- [ ] **Step 7: Run all three tests together**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdateProposedTaskHandlerTests"
```

Expected: 3 tests passed (`UpdateTask_ModifiesFields`, `UpdateTaskStatus_ApprovesTask`, `AddTask_CreatesManualTask`).

- [ ] **Step 8: Build the application project**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 9: Run `dotnet format` on changed files**

Run:

```bash
dotnet format backend/Anela.Heblo.sln --include \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskRequest.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskResponse.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs
```

Expected: exits 0.

- [ ] **Step 10: Commit**

Run:

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs
git commit -m "feat: add AddProposedTask handler for manually appending tasks"
```

---

## Task 4: Final validation

**Files:** No file changes — verification only.

- [ ] **Step 1: Build the full backend solution**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` across all projects.

- [ ] **Step 2: Run all MeetingTasks tests (new + existing siblings) together**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.MeetingTasks"
```

Expected: all tests pass, including the three new ones and the prior `GetTranscriptDetailHandlerTests`, `GetTranscriptListHandlerTests`, `IngestPlaudRecordingHandlerTests`. No regressions.

- [ ] **Step 3: Run the full backend test suite as a regression net**

Run:

```bash
dotnet test backend/Anela.Heblo.sln
```

Expected: all tests pass. If any unrelated test fails, investigate before reporting completion — it likely indicates the rebase pulled in flaky tests from the epic, not an issue introduced by this work.

- [ ] **Step 4: Run `dotnet format` over the whole solution**

Run:

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exits 0. If it reports unformatted files among the new files, run `dotnet format backend/Anela.Heblo.sln` (without `--verify-no-changes`), commit the formatting fix as a follow-up `chore:` commit, then re-run with `--verify-no-changes`.

- [ ] **Step 5: Verify PR target — confirm the branch is up to date with the epic**

Run:

```bash
git fetch origin feat/meeting-task-validation-epic
git merge-base --is-ancestor origin/feat/meeting-task-validation-epic HEAD && echo OK || echo STALE
```

Expected: `OK`. If `STALE`, rebase onto the latest epic tip before opening the PR.

- [ ] **Step 6: No commit for this task**

This task is verification-only.

---

## Specification Amendments Incorporated

The architecture review identified amendments to the original spec. They are baked into this plan:

1. `ErrorCodes.NotFound` → `ErrorCodes.ResourceNotFound` everywhere (the actual enum member, used by sibling `GetTranscriptDetailHandler`).
2. Test helper `CreateTranscriptWithTask` uses real `MeetingTranscript` fields: `PlaudRecordingId`, `PlaudCreatedAt`, `Subject`, `Summary`, `RawTranscript`, `Status`, `ReceivedAt`. The spec's `SourceEmail` field does not exist on the entity.
3. Requests are typed `IRequest<TConcreteResponse>` (e.g., `IRequest<UpdateProposedTaskResponse>`), not `IRequest<BaseResponse>`, so callers see the concrete shape.
4. Handlers inject `ILogger<THandler>` and emit `LogInformation` on entry / `LogWarning` on not-found / invalid status. Matches `GetTranscriptDetailHandler` and `IngestPlaudRecordingHandler`.
5. Each response lives in its own `*Response.cs` file under the UseCase folder.
6. Error responses use the constructor-overload form: `new UpdateProposedTaskResponse(ErrorCodes.ResourceNotFound)`.
7. Tests use FluentAssertions + `NullLogger<THandler>.Instance` + `Mock.Verify(r => r.SaveChangesAsync(...), Times.Once)` — matches `GetTranscriptDetailHandlerTests`.
8. `AddProposedTaskHandler` explicitly sets `ExternalTaskId = null` on the response DTO for symmetry with the read handler.

## Out-of-Scope Items (Documented in Spec)

These are not implemented by this plan:

- MVC controller endpoints
- Authorization checks (handled at the controller layer)
- Frontend UI
- Status transition rules (e.g., disallowing Approved→Pending) — handler accepts any valid `ProposedTaskStatus`
- Domain events, audit logging, notifications
- Validation that the transcript is in `PendingReview` before edits
- Bulk approve/reject
- Optimistic concurrency / `RowVersion`
- E2E tests
- OpenAPI/Swagger DTO surface
- Updating `transcript.Status` / `ReviewedAt` / `ReviewedByUser` when all tasks are reviewed
