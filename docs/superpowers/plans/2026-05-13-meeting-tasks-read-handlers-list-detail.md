# Meeting Tasks Read Handlers — List & Detail Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement two MediatR read handlers (`GetTranscriptList`, `GetTranscriptDetail`) and their DTOs for the Meeting Task Validation Checkpoint epic, backed by the already-registered `IMeetingTranscriptRepository`.

**Architecture:** Vertical-slice layout under `Anela.Heblo.Application/Features/MeetingTasks/`. Two class DTOs in `Contracts/` (`MeetingTranscriptDto`, `ProposedTaskDto`). Two MediatR `Request`/`Response`/`Handler` trios under `UseCases/`. Responses inherit from `BaseResponse`. MediatR assembly scanning (`ApplicationModule.cs:52`) picks up the new handlers automatically; the repository is already wired in `PersistenceModule.cs:131`. No new DI module file is required.

**Tech Stack:** .NET 8, C# 12 with nullable reference types, MediatR (assembly-scanned), xUnit + Moq + FluentAssertions for tests.

---

## Branch & PR Targeting

- **Working branch:** `feat-meeting-tasks-read-handlers-list-detail` (already checked out in the worktree).
- **PR target branch:** `feat/meeting-task-validation-epic` (NOT `main`). The epic branch is the integration line for all sub-features in this epic; the prior persistence subtask landed on the same branch.

---

## File Map

**Application layer — create:**
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/ProposedTaskDto.cs` — child DTO (class, OpenAPI-safe).
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs` — aggregate DTO (class) reused by list (empty `Tasks`) and detail (populated `Tasks`).
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailHandler.cs`

**Tests — create:**
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptDetailHandlerTests.cs`

**Out of scope — do NOT create:**
- `MeetingTasksModule.cs` — MediatR handler discovery is automatic and the repository is already registered in `PersistenceModule.AddPersistenceServices`.
- Any MVC controller, HTTP route, or OpenAPI wiring.
- Mutation handlers (Approve/Reject/AddManualTask). They are separate epic subtasks.
- Any frontend / generated TypeScript client changes — this PR is backend only.
- Any change to `MeetingTranscriptRepository` or the EF model.

---

## Conventions Reference (project-specific, must follow)

- **DTOs are `class`, never `record`.** OpenAPI generators mishandle record parameter order — see `CLAUDE.md` and `docs/architecture/development_guidelines.md`. Internal domain types may still be records; DTOs cannot.
- **Non-nullable string properties use `= null!;`** to match the domain entities (`MeetingTranscript.cs:6-10`).
- **Collections initialised inline** to empty (`= new();` or `= new List<T>();`).
- **Responses inherit `BaseResponse`.** Default state is `Success = true`; setting `Success = false` requires also setting `ErrorCode`. See `backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs`.
- **Not-found uses `ErrorCodes.ResourceNotFound`** (`ErrorCodes.cs:25`, HTTP 404). The spec's reference to `ErrorCodes.NotFound` is a typo — there is no such enum member. The architecture review amends this; use `ResourceNotFound`.
- **Status fields on DTOs are `string`,** populated via `Enum.ToString()` (consistent with `GetTransportBoxesHandler.cs:57` and every other handler in the codebase).
- **Constructor injection only.** Inject `IMeetingTranscriptRepository` (already DI-registered as scoped in `PersistenceModule.cs:131`). Optionally inject `ILogger<T>` to match sibling handlers — see Task 3.
- **Repository signatures (do not invent):**
  - `Task<MeetingTranscript?> GetByIdAsync(Guid id, CancellationToken ct = default)`
  - `Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(MeetingTranscriptStatus? statusFilter, int page, int pageSize, CancellationToken ct = default)`

---

### Task 1: ProposedTaskDto

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/ProposedTaskDto.cs`

**Rationale:** Smallest leaf type — `MeetingTranscriptDto.Tasks` references it, so define it first.

- [ ] **Step 1: Create the DTO file**

Write `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/ProposedTaskDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Contracts;

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
```

- [ ] **Step 2: Build the Application project to confirm the DTO compiles**

Run from the worktree root:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded.` with 0 errors. Warnings are acceptable as long as the project builds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/ProposedTaskDto.cs
git commit -m "feat: add ProposedTaskDto for MeetingTasks read handlers"
```

---

### Task 2: MeetingTranscriptDto

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs`

**Rationale:** Aggregate DTO reused by both list (with `Tasks` left empty, only counts populated) and detail (with `Tasks` fully populated). `RawTranscript` is deliberately excluded from this subtask (the spec's *Out of Scope* explicitly defers it).

- [ ] **Step 1: Create the DTO file**

Write `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Contracts;

public class MeetingTranscriptDto
{
    public Guid Id { get; set; }
    public string PlaudRecordingId { get; set; } = null!;
    public DateTime PlaudCreatedAt { get; set; }
    public string Subject { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime ReceivedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUser { get; set; }
    public int TaskCount { get; set; }
    public int ApprovedTaskCount { get; set; }
    public int RejectedTaskCount { get; set; }
    public List<ProposedTaskDto> Tasks { get; set; } = new();
}
```

- [ ] **Step 2: Build the Application project**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs
git commit -m "feat: add MeetingTranscriptDto for MeetingTasks read handlers"
```

---

### Task 3: GetTranscriptList — Request, Response, failing test

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListResponse.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs`

**Rationale:** TDD discipline — define the contract types referenced by the test, write the failing test, then implement the handler. Defining `Request`/`Response` first is not "implementation" — it is the contract surface the test compiles against.

- [ ] **Step 1: Create the request**

Write `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;

public class GetTranscriptListRequest : IRequest<GetTranscriptListResponse>
{
    public string? StatusFilter { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
```

- [ ] **Step 2: Create the response**

Write `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;

public class GetTranscriptListResponse : BaseResponse
{
    public List<MeetingTranscriptDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}
```

`TotalPages` is a computed `get`-only property so the handler never has to set it explicitly and consumers always see a value consistent with `TotalCount` and `PageSize`.

- [ ] **Step 3: Build the Application project**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Write the failing test for the list handler**

Write `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;
using Anela.Heblo.Domain.Features.MeetingTasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class GetTranscriptListHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repositoryMock;
    private readonly GetTranscriptListHandler _handler;

    public GetTranscriptListHandlerTests()
    {
        _repositoryMock = new Mock<IMeetingTranscriptRepository>();
        _handler = new GetTranscriptListHandler(
            _repositoryMock.Object,
            NullLogger<GetTranscriptListHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ReturnsPagedResults()
    {
        // Arrange
        var transcriptId = Guid.NewGuid();
        var transcript = new MeetingTranscript
        {
            Id = transcriptId,
            PlaudRecordingId = "rec-001",
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
                    Id = Guid.NewGuid(),
                    MeetingTranscriptId = transcriptId,
                    Title = "Write spec",
                    Description = "Draft the spec",
                    Assignee = "ondra",
                    Status = ProposedTaskStatus.Pending,
                    IsManuallyAdded = false
                }
            }
        };

        _repositoryMock
            .Setup(r => r.GetListAsync(
                It.IsAny<MeetingTranscriptStatus?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<MeetingTranscript> { transcript }, 1));

        var request = new GetTranscriptListRequest
        {
            PageNumber = 1,
            PageSize = 20
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalCount.Should().Be(1);
        result.Items.Should().HaveCount(1);

        var item = result.Items[0];
        item.Id.Should().Be(transcriptId);
        item.PlaudRecordingId.Should().Be("rec-001");
        item.Subject.Should().Be("Sprint Planning");
        item.Status.Should().Be("PendingReview");
        item.TaskCount.Should().Be(1);
        item.ApprovedTaskCount.Should().Be(0);
        item.RejectedTaskCount.Should().Be(0);
        item.Tasks.Should().BeEmpty();
    }
}
```

- [ ] **Step 5: Run the test to verify it fails (compile error — handler does not exist yet)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GetTranscriptListHandlerTests"
```

Expected: build failure with error `CS0246: The type or namespace name 'GetTranscriptListHandler' could not be found`. This proves the test exercises the handler we are about to write.

---

### Task 4: GetTranscriptListHandler implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListHandler.cs`

**Rationale:** Minimal implementation to make the failing test from Task 3 pass. `StatusFilter` parsing is silent (invalid/empty → no filter), matching `GetTransportBoxesHandler.cs:30-41`. Task counts are computed in-memory from the already-eager-loaded `Tasks` collection (`MeetingTranscriptRepository.cs:38`). `Tasks` collection on the list DTO is left empty (counts-only view).

- [ ] **Step 1: Create the handler**

Write `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;

public class GetTranscriptListHandler : IRequestHandler<GetTranscriptListRequest, GetTranscriptListResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly ILogger<GetTranscriptListHandler> _logger;

    public GetTranscriptListHandler(
        IMeetingTranscriptRepository repository,
        ILogger<GetTranscriptListHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetTranscriptListResponse> Handle(GetTranscriptListRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Getting meeting transcript list — StatusFilter: {StatusFilter}, PageNumber: {PageNumber}, PageSize: {PageSize}",
            request.StatusFilter, request.PageNumber, request.PageSize);

        MeetingTranscriptStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(request.StatusFilter)
            && Enum.TryParse<MeetingTranscriptStatus>(request.StatusFilter, ignoreCase: true, out var parsed))
        {
            statusFilter = parsed;
        }

        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter,
            request.PageNumber,
            request.PageSize,
            cancellationToken);

        var dtos = items.Select(t => new MeetingTranscriptDto
        {
            Id = t.Id,
            PlaudRecordingId = t.PlaudRecordingId,
            PlaudCreatedAt = t.PlaudCreatedAt,
            Subject = t.Subject,
            Summary = t.Summary,
            Status = t.Status.ToString(),
            ReceivedAt = t.ReceivedAt,
            ReviewedAt = t.ReviewedAt,
            ReviewedByUser = t.ReviewedByUser,
            TaskCount = t.Tasks.Count,
            ApprovedTaskCount = t.Tasks.Count(x => x.Status == ProposedTaskStatus.Approved),
            RejectedTaskCount = t.Tasks.Count(x => x.Status == ProposedTaskStatus.Rejected),
            Tasks = new()
        }).ToList();

        return new GetTranscriptListResponse
        {
            Items = dtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
```

- [ ] **Step 2: Run the test to verify it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GetTranscriptListHandlerTests"
```

Expected: `Passed!  - Failed: 0, Passed: 1`.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs
git commit -m "feat: add GetTranscriptList read handler for MeetingTasks"
```

---

### Task 5: GetTranscriptDetail — Request, Response, failing happy-path test

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailResponse.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptDetailHandlerTests.cs`

**Rationale:** Same TDD pattern as the list handler. The response holds a `MeetingTranscriptDto Transcript` field (singular) per the spec; it is `null!` by default so callers must check `Success` before dereferencing it (this is fine — `GetTransportBoxByIdResponse.cs` follows the same pattern).

- [ ] **Step 1: Create the request**

Write `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;

public class GetTranscriptDetailRequest : IRequest<GetTranscriptDetailResponse>
{
    public Guid Id { get; set; }
}
```

- [ ] **Step 2: Create the response**

Write `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;

public class GetTranscriptDetailResponse : BaseResponse
{
    public MeetingTranscriptDto Transcript { get; set; } = null!;
}
```

- [ ] **Step 3: Build the Application project**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Write the failing tests for the detail handler**

Write `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptDetailHandlerTests.cs`. Both tests go into the same file so they share construction; they will both fail at compile time until the handler exists.

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class GetTranscriptDetailHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repositoryMock;
    private readonly GetTranscriptDetailHandler _handler;

    public GetTranscriptDetailHandlerTests()
    {
        _repositoryMock = new Mock<IMeetingTranscriptRepository>();
        _handler = new GetTranscriptDetailHandler(
            _repositoryMock.Object,
            NullLogger<GetTranscriptDetailHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ExistingTranscript_ReturnsDetailWithTasks()
    {
        // Arrange
        var transcriptId = Guid.NewGuid();
        var transcript = new MeetingTranscript
        {
            Id = transcriptId,
            PlaudRecordingId = "rec-001",
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
                    Id = Guid.NewGuid(),
                    MeetingTranscriptId = transcriptId,
                    Title = "Write spec",
                    Description = "Draft the spec",
                    Assignee = "ondra",
                    Status = ProposedTaskStatus.Pending,
                    IsManuallyAdded = false
                }
            }
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        var request = new GetTranscriptDetailRequest { Id = transcriptId };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Transcript.Should().NotBeNull();
        result.Transcript.Id.Should().Be(transcriptId);
        result.Transcript.Subject.Should().Be("Sprint Planning");
        result.Transcript.PlaudRecordingId.Should().Be("rec-001");
        result.Transcript.Status.Should().Be("PendingReview");
        result.Transcript.Tasks.Should().HaveCount(1);
        result.Transcript.Tasks[0].Title.Should().Be("Write spec");
        result.Transcript.Tasks[0].Status.Should().Be("Pending");
    }

    [Fact]
    public async Task Handle_NonExistentTranscript_ReturnsNotFound()
    {
        // Arrange
        var missingId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        var request = new GetTranscriptDetailRequest { Id = missingId };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
    }
}
```

- [ ] **Step 5: Run the tests to verify they fail (handler does not exist yet)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GetTranscriptDetailHandlerTests"
```

Expected: build failure with error `CS0246: The type or namespace name 'GetTranscriptDetailHandler' could not be found`. The handler class is the next task's responsibility.

---

### Task 6: GetTranscriptDetailHandler implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailHandler.cs`

**Rationale:** Minimal implementation to satisfy both detail tests. Null repository result returns a failure response with `ErrorCode = ErrorCodes.ResourceNotFound` (the existing 404 code in `ErrorCodes.cs:25`). Tasks are projected to `ProposedTaskDto` and fully populated in detail (the only difference from list mode).

- [ ] **Step 1: Create the handler**

Write `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;

public class GetTranscriptDetailHandler : IRequestHandler<GetTranscriptDetailRequest, GetTranscriptDetailResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly ILogger<GetTranscriptDetailHandler> _logger;

    public GetTranscriptDetailHandler(
        IMeetingTranscriptRepository repository,
        ILogger<GetTranscriptDetailHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetTranscriptDetailResponse> Handle(GetTranscriptDetailRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting meeting transcript detail — Id: {Id}", request.Id);

        var transcript = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {Id} not found", request.Id);
            return new GetTranscriptDetailResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ResourceNotFound
            };
        }

        var dto = new MeetingTranscriptDto
        {
            Id = transcript.Id,
            PlaudRecordingId = transcript.PlaudRecordingId,
            PlaudCreatedAt = transcript.PlaudCreatedAt,
            Subject = transcript.Subject,
            Summary = transcript.Summary,
            Status = transcript.Status.ToString(),
            ReceivedAt = transcript.ReceivedAt,
            ReviewedAt = transcript.ReviewedAt,
            ReviewedByUser = transcript.ReviewedByUser,
            TaskCount = transcript.Tasks.Count,
            ApprovedTaskCount = transcript.Tasks.Count(x => x.Status == ProposedTaskStatus.Approved),
            RejectedTaskCount = transcript.Tasks.Count(x => x.Status == ProposedTaskStatus.Rejected),
            Tasks = transcript.Tasks.Select(t => new ProposedTaskDto
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                Assignee = t.Assignee,
                DueDate = t.DueDate,
                Status = t.Status.ToString(),
                ExternalTaskId = t.ExternalTaskId,
                IsManuallyAdded = t.IsManuallyAdded
            }).ToList()
        };

        return new GetTranscriptDetailResponse { Transcript = dto };
    }
}
```

- [ ] **Step 2: Run both detail tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GetTranscriptDetailHandlerTests"
```

Expected: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptDetailHandlerTests.cs
git commit -m "feat: add GetTranscriptDetail read handler for MeetingTasks"
```

---

### Task 7: Final validation — full build, format, all targeted tests, full test suite

**Files:** (no new files — validation only)

**Rationale:** The per-task tests prove the handlers behave correctly in isolation; this task confirms nothing else regressed and code style is clean before opening the PR.

- [ ] **Step 1: Run both new test classes together**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GetTranscriptListHandlerTests|FullyQualifiedName~GetTranscriptDetailHandlerTests"
```

Expected: `Passed!  - Failed: 0, Passed: 3`.

- [ ] **Step 2: Build the entire backend solution**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with 0 errors. Warning count should not increase compared to the pre-change baseline (run `git stash && dotnet build backend/Anela.Heblo.sln && git stash pop` if a baseline is needed).

- [ ] **Step 3: Run the entire backend test suite**

```bash
dotnet test backend/Anela.Heblo.sln --no-build
```

Expected: all previously passing tests still pass, plus the three new ones. Any pre-existing test failure unrelated to this change should be flagged in the PR description but does not block this task.

- [ ] **Step 4: Apply formatter**

```bash
dotnet format backend/Anela.Heblo.sln
```

Expected: exits cleanly. If files were reformatted, inspect the diff (`git diff`) — every change must be limited to the eight files this PR creates. If the formatter touched unrelated files, revert those reverts (`git checkout --` only on files that are not in the File Map) so the PR stays surgical.

- [ ] **Step 5: Commit any formatter changes**

```bash
git status
# If `git status` shows only files from this PR, commit them:
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptDetailHandlerTests.cs
git commit -m "chore: apply dotnet format to MeetingTasks read handlers"
```

If `git status` is clean after Step 4, skip this step.

- [ ] **Step 6: Final sanity check**

```bash
git log --oneline -10
git status
```

Expected: clean working tree, commits from Tasks 1–7 visible in `git log`, branch ready for push and PR creation against `feat/meeting-task-validation-epic`.

---

## Self-Review Cross-Check (run by plan author before handoff)

**Spec coverage:**
- FR-1 (`MeetingTranscriptDto`) — Task 2.
- FR-2 (`ProposedTaskDto`) — Task 1.
- FR-3 (`GetTranscriptListHandler`) — Tasks 3 (contract + failing test) and 4 (implementation).
- FR-4 (`GetTranscriptDetailHandler`) — Tasks 5 (contract + failing tests) and 6 (implementation).
- FR-5 (unit tests) — Tasks 3 and 5 author the test classes; Task 7 runs them as part of full validation.
- NFR-1 (performance / repository-side paging) — handler delegates paging to `GetListAsync` and computes counts off the eager-loaded collection, no extra roundtrip. Documented in Task 4 rationale.
- NFR-2 (security) — handlers perform no authorization; out of scope as the spec states.
- NFR-3 (architectural conformance) — class DTOs, `BaseResponse` inheritance, MediatR contract, constructor injection only, no module file. Reinforced in the Conventions Reference and File Map.
- NFR-4 (BE validation gate) — Task 7 Steps 2 and 4.

**Type / signature consistency:**
- `MeetingTranscriptDto.Tasks` is `List<ProposedTaskDto>` (Task 2) — matches what `GetTranscriptListHandler` (Task 4) assigns as `new()` and what `GetTranscriptDetailHandler` (Task 6) projects with `.ToList()`.
- `GetListAsync` return tuple destructured as `(items, totalCount)` in Task 4 matches the interface signature in `IMeetingTranscriptRepository.cs:7-11`.
- `ErrorCodes.ResourceNotFound` (Task 6) matches the existing enum member (`ErrorCodes.cs:25`) — corrects the spec's typo per the architecture review.
- Test method names match the spec exactly: `Handle_ReturnsPagedResults`, `Handle_ExistingTranscript_ReturnsDetailWithTasks`, `Handle_NonExistentTranscript_ReturnsNotFound`.

**Placeholder scan:** no `TBD`, `TODO`, "implement later", or "similar to Task N" references. Every step that changes code contains the complete code body.
