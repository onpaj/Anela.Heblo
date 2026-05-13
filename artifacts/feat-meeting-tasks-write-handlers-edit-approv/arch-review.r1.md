I have enough context. Writing the architecture review now.

# Architecture Review: Meeting Task Write Handlers — Edit, Approve/Reject, Add Task

## Skip Design: true

Backend-only MediatR handlers with no UI components, screens, or visual changes. UI work is explicitly out of scope (handled in a later subtask).

## Architectural Fit Assessment

The feature is a clean addition to the existing Vertical Slice in `Features/MeetingTasks/UseCases/`. Three sibling write/read handlers already exist (`IngestPlaudRecording`, `GetTranscriptList`, `GetTranscriptDetail`) and define the conventions to follow: one folder per UseCase, request/response/handler split into three files, `IRequestHandler<TRequest, TResponse>` with constructor-injected `IMeetingTranscriptRepository` and `ILogger<THandler>`, `BaseResponse`-derived responses with an `ErrorCodes` constructor overload.

**Two material mismatches between the spec and the existing codebase must be resolved before implementation:**

1. **`ErrorCodes.NotFound` does not exist.** The enum member is `ResourceNotFound` (`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:25`), and that is what `GetTranscriptDetailHandler` uses. The spec's `ErrorCodes.NotFound` references will fail to compile.
2. **`MeetingTranscript.SourceEmail` does not exist.** The actual entity (`backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs` on `origin/feat/meeting-task-validation-epic`) has `PlaudRecordingId`, `PlaudCreatedAt`, `Subject`, `Summary`, `RawTranscript`, `Status`, `ReceivedAt`, `ReviewedAt`, `ReviewedByUser`, `Tasks`. The spec's test helper sets `SourceEmail` — this won't compile and is missing two required scalars (`PlaudRecordingId`, `RawTranscript`) needed to mirror the sibling test's instantiation pattern.

A third prerequisite is also unmet: **this branch was not created from `feat/meeting-task-validation-epic`.** It was branched from `main`, so the entire domain model (`MeetingTranscript`, `ProposedTask`, `ProposedTaskStatus`, `IMeetingTranscriptRepository`) and contract (`ProposedTaskDto`) do not exist on disk in this worktree. The brief is explicit that the epic branch is the required base.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│  Features/MeetingTasks/UseCases/                                     │
│                                                                      │
│   UpdateProposedTask/                  UpdateProposedTaskStatus/     │
│   ├─ Request   ──┐                     ├─ Request    ──┐             │
│   ├─ Response    ├─► Handler           ├─ Response     ├─► Handler   │
│   └─ Handler  ───┘     │               └─ Handler   ───┘     │       │
│                        ▼                                     ▼       │
│   AddProposedTask/    [IMeetingTranscriptRepository]                 │
│   ├─ Request   ──┐     ▲                                     ▲       │
│   ├─ Response    ├─► Handler ────────────────────────────────┘       │
│   └─ Handler  ───┘                                                   │
│                                                                      │
│   Contracts/ProposedTaskDto  ◄─── AddProposedTaskResponse.Task       │
└──────────────────────────────────────────────────────────────────────┘
                                  │
                                  ▼
              Domain.Features.MeetingTasks (aggregate + enum + repo iface)
                                  │
                                  ▼
              Persistence.MeetingTasks.MeetingTranscriptRepository
                                  │
                                  ▼
                       ApplicationDbContext (EF Core)
```

All three handlers are auto-registered by MediatR assembly scan via the existing `AddMeetingTasksModule` registration; no DI changes are needed.

### Key Design Decisions

#### Decision 1: Response file granularity
**Options considered:** (a) Define response class in the same file as the handler (spec's approach); (b) Three files per UseCase folder — `*Request.cs`, `*Response.cs`, `*Handler.cs` (sibling convention).

**Chosen approach:** Three files per UseCase. Each response gets its own file.

**Rationale:** Every existing UseCase under `MeetingTasks/` (and across the codebase based on the sibling pattern) keeps the response in a dedicated file. The spec embedding `UpdateProposedTaskResponse` inside `UpdateProposedTaskHandler.cs` is the only deviation — keeping it consistent costs nothing and avoids the "find the response type" friction. This also matches the constructor-overload pattern (`public Foo() {} public Foo(ErrorCodes e) : base(e) {}`) the siblings use.

#### Decision 2: How to signal not-found / validation failures
**Options considered:** (a) Property setter (`new Response { Success = false, ErrorCode = ErrorCodes.ResourceNotFound }`); (b) Constructor overload (`new Response(ErrorCodes.ResourceNotFound)`) matching `GetTranscriptDetailResponse`.

**Chosen approach:** Constructor overload. Mirror `GetTranscriptDetailResponse`:
```csharp
public class UpdateProposedTaskResponse : BaseResponse
{
    public UpdateProposedTaskResponse() { }
    public UpdateProposedTaskResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

**Rationale:** `BaseResponse` ships a protected error-code constructor specifically for this. Using it is the documented intent and keeps call sites terse. The setter form works but drifts from the immediate sibling.

#### Decision 3: Whether handlers should log
**Options considered:** (a) No logger (spec); (b) Inject `ILogger<THandler>` and log at the same level as siblings.

**Chosen approach:** Inject `ILogger<THandler>` and emit one `LogInformation` on success and one `LogWarning` on not-found, matching `GetTranscriptDetailHandler` and `IngestPlaudRecordingHandler`.

**Rationale:** Both sibling handlers log; observability for write operations is more valuable than for reads. Cost is one extra ctor parameter and two log lines per handler.

#### Decision 4: Error code value (`NotFound` vs `ResourceNotFound`)
**Options considered:** (a) Use `ErrorCodes.NotFound` as the spec says; (b) Use `ErrorCodes.ResourceNotFound` as actually defined.

**Chosen approach:** `ErrorCodes.ResourceNotFound`. Treat this as a spec correction.

**Rationale:** `NotFound` is not a member of the `ErrorCodes` enum. The sibling `GetTranscriptDetailHandler` already uses `ResourceNotFound` (HTTP 404). This is the only correct choice that compiles and matches existing handler semantics for the same condition.

#### Decision 5: Test framework idioms
**Options considered:** (a) xUnit + `Assert.*` (spec); (b) xUnit + FluentAssertions + `NullLogger<T>` (sibling test convention).

**Chosen approach:** Match the sibling test (`GetTranscriptDetailHandlerTests`): FluentAssertions for assertions, `NullLogger<THandler>.Instance` for the logger, and `_repositoryMock.Verify(r => r.SaveChangesAsync(...))` to assert persistence was actually called.

**Rationale:** Consistency with the existing test file ten lines away in the same folder. FluentAssertions is already a referenced package and idiomatic across the project.

## Implementation Guidance

### Directory / Module Structure

Files (paths exactly as the spec lists) plus the three new `*Response.cs` files:

```
backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/
├── UpdateProposedTask/
│   ├── UpdateProposedTaskRequest.cs
│   ├── UpdateProposedTaskResponse.cs   ← add (not in spec)
│   └── UpdateProposedTaskHandler.cs
├── UpdateProposedTaskStatus/
│   ├── UpdateProposedTaskStatusRequest.cs
│   ├── UpdateProposedTaskStatusResponse.cs   ← add
│   └── UpdateProposedTaskStatusHandler.cs
└── AddProposedTask/
    ├── AddProposedTaskRequest.cs
    ├── AddProposedTaskResponse.cs   ← add (currently embedded with Request in spec)
    └── AddProposedTaskHandler.cs

backend/test/Anela.Heblo.Tests/Features/MeetingTasks/
└── UpdateProposedTaskHandlerTests.cs
```

No changes to `MeetingTasksModule.cs` (MediatR scans the assembly). No changes to `IMeetingTranscriptRepository` (existing `GetByIdAsync` + `SaveChangesAsync` suffice; `EF Core change tracking` handles edits and the collection append).

### Interfaces and Contracts

**Requests** (MVC binding will validate `[Required]`):
```csharp
public class UpdateProposedTaskRequest : IRequest<UpdateProposedTaskResponse>
{
    public Guid TranscriptId { get; set; }
    public Guid TaskId { get; set; }
    [Required] public string Title { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    [Required] public string Assignee { get; set; } = null!;
    public DateTime? DueDate { get; set; }
}

public class UpdateProposedTaskStatusRequest : IRequest<UpdateProposedTaskStatusResponse>
{
    public Guid TranscriptId { get; set; }
    public Guid TaskId { get; set; }
    [Required] public string Status { get; set; } = null!;
}

public class AddProposedTaskRequest : IRequest<AddProposedTaskResponse>
{
    public Guid TranscriptId { get; set; }
    [Required] public string Title { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    [Required] public string Assignee { get; set; } = null!;
    public DateTime? DueDate { get; set; }
}
```

> Note: `IRequest<BaseResponse>` (per spec) works at runtime but is weaker than `IRequest<UpdateProposedTaskResponse>`. Type the request generic over the concrete response class so callers and the OpenAPI surface (future) see the concrete shape. This still satisfies the spec's "Response type" requirement.

**Responses:**
```csharp
public class UpdateProposedTaskResponse : BaseResponse
{
    public UpdateProposedTaskResponse() { }
    public UpdateProposedTaskResponse(ErrorCodes errorCode) : base(errorCode) { }
}
// Same shape for UpdateProposedTaskStatusResponse.

public class AddProposedTaskResponse : BaseResponse
{
    public AddProposedTaskResponse() { }
    public AddProposedTaskResponse(ErrorCodes errorCode) : base(errorCode) { }
    public ProposedTaskDto Task { get; set; } = null!;
}
```

`ProposedTaskDto` (already exists) has eight fields — when populating in `AddProposedTaskHandler`, also set `ExternalTaskId = null` explicitly or rely on default. (Spec lists only seven fields for the response DTO; ExternalTaskId on a brand-new manual task is `null` — assign `task.ExternalTaskId` for symmetry with the read handler's mapping.)

### Data Flow

```
UpdateProposedTask:
  client → controller (later subtask) → MediatR
    → Handler.GetByIdAsync(TranscriptId)
      missing → Response(ResourceNotFound)
    → transcript.Tasks.FirstOrDefault(t => t.Id == TaskId)
      missing → Response(ResourceNotFound)
    → mutate Title, Description, Assignee, DueDate on the tracked entity
    → SaveChangesAsync(ct)   // EF Core emits an UPDATE on ProposedTask row
    → Response()              // Success = true

UpdateProposedTaskStatus:
  ... same load/locate guard ...
    → Enum.TryParse<ProposedTaskStatus>(Status, ignoreCase: true, out var s)
      false → Response(ValidationError)
    → task.Status = s
    → SaveChangesAsync(ct)
    → Response()

AddProposedTask:
  ... transcript load guard ...
    → construct ProposedTask { Id=NewGuid, MeetingTranscriptId=transcript.Id,
                                Status=Pending, IsManuallyAdded=true, ... }
    → transcript.Tasks.Add(task)   // EF tracks add via aggregate
    → SaveChangesAsync(ct)         // INSERT on ProposedTask
    → Response { Task = ProposedTaskDto.From(task) }
```

EF Core change tracking persists in-place edits because `GetByIdAsync` returns a tracked entity (verified via the existing `MeetingTranscriptRepository` + `GetTranscriptDetailHandler` pattern). No explicit `Update` call is needed.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Branch is rooted on `main` and the entire prerequisite chain (domain entities, repository, DTO) is absent. Implementation cannot start. | Critical | Rebase or reset this branch on top of `origin/feat/meeting-task-validation-epic` before any file is written. PR target must be `feat/meeting-task-validation-epic`. |
| Spec uses non-existent `ErrorCodes.NotFound`. Copying the brief code verbatim will not compile. | High | Use `ErrorCodes.ResourceNotFound` (HTTP 404) for both transcript-missing and task-missing cases — matches the sibling handler. |
| Spec uses non-existent `MeetingTranscript.SourceEmail` in the test helper and omits required `PlaudRecordingId` / `RawTranscript` fields. Test class will not compile. | High | Drop `SourceEmail`; populate `PlaudRecordingId = "rec-xyz"` and `RawTranscript = ""` to mirror the sibling test (`GetTranscriptDetailHandlerTests`). |
| `IRequest<BaseResponse>` (spec) weakens caller typing — controllers and any future client see only the base. | Low | Type each request with its concrete response class. |
| `Enum.TryParse<ProposedTaskStatus>` will accept integer strings (`"1"`, `"2"`) because of `Enum.TryParse` semantics. Spec only contemplates name strings. | Low | Either accept this as harmless (still maps to a valid enum value) or follow with `Enum.IsDefined(typeof(ProposedTaskStatus), newStatus)`. Recommend accepting — current behavior parses "1" as `Pending`, which is no worse than the controller doing the same. |
| No optimistic concurrency. Two concurrent edits on the same task last-write-wins. | Low (out of scope) | Documented as out of scope. Revisit when controller + audit are added. |
| Spec says nothing about updating `transcript.Status` or `ReviewedAt`/`ReviewedByUser` when all tasks are approved/rejected. | Medium | Confirmed out of scope by spec ("Status transition rules" and "audit logging" are excluded). Note this for the controller/UI subtask. |
| `transcript.Tasks.Add(task)` while a referenced navigation property `MeetingTranscript` is `null!`. EF Core sets the FK from `MeetingTranscriptId` so this is fine, but tests reading `task.MeetingTranscript` would NRE. | Low | Set `MeetingTranscriptId = transcript.Id` (as spec does) and do not touch `task.MeetingTranscript` in tests. |
| Logger added but not asserted in tests. | Low | Use `NullLogger<THandler>.Instance` and skip assertions; verifying log output is not required by the spec. |

## Specification Amendments

1. **Replace every `ErrorCodes.NotFound` reference with `ErrorCodes.ResourceNotFound`** in FR-1, FR-2, FR-3, the response code samples in the brief, and the acceptance criteria. This is the only existing 404-mapped code in `ErrorCodes`.

2. **Remove `SourceEmail` from the test helper** in FR-4. The field does not exist on `MeetingTranscript`. Replace with the actual required scalars: `PlaudRecordingId = "rec-test"`, `PlaudCreatedAt = DateTime.UtcNow`, `RawTranscript = ""`. `Subject`, `Summary`, `Status`, `ReceivedAt` stay.

3. **Type request generics over concrete response classes** (FR-1 and FR-2): `IRequest<UpdateProposedTaskResponse>` and `IRequest<UpdateProposedTaskStatusResponse>` instead of `IRequest<BaseResponse>`. FR-3 already does this.

4. **Add `ILogger<THandler>` constructor dependency** to each handler and emit `LogInformation` on success / `LogWarning` on not-found / `LogWarning` on invalid status (FR-2). Matches sibling convention. NFR-1 unaffected.

5. **Split each response into its own `*Response.cs` file** under the UseCase folder (FR-5). Update file layout list:
   - Add `UpdateProposedTaskResponse.cs`
   - Add `UpdateProposedTaskStatusResponse.cs`
   - Move `AddProposedTaskResponse` out of `AddProposedTaskRequest.cs` into `AddProposedTaskResponse.cs`

6. **Use constructor-overload error responses** (`new UpdateProposedTaskResponse(ErrorCodes.ResourceNotFound)`) instead of object-initializer (`new UpdateProposedTaskResponse { Success = false, ErrorCode = ... }`). Matches `GetTranscriptDetailResponse`.

7. **Test conventions:** use FluentAssertions, `NullLogger<THandler>.Instance`, and `Mock.Verify` to assert `SaveChangesAsync` is invoked exactly once on the success paths. Matches `GetTranscriptDetailHandlerTests`.

8. **`AddProposedTaskHandler` should populate `ExternalTaskId = null` on the returned `ProposedTaskDto`** for symmetry with `GetTranscriptDetailHandler.Tasks.Select(...)`. The DTO field is nullable so this is purely about completeness.

## Prerequisites

Before any handler code is written:

1. **Rebase or recreate this branch from `origin/feat/meeting-task-validation-epic`.** Current branch base is `main`, which is missing:
   - `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/*` (4 files: entities, enums, repository interface)
   - `backend/src/Anela.Heblo.Application/Features/MeetingTasks/*` (module, contracts, services, existing UseCases — `IngestPlaudRecording`, `GetTranscriptList`, `GetTranscriptDetail`)
   - `backend/src/Anela.Heblo.Persistence/MeetingTasks/*` (EF configurations, repository implementation)
   - EF migration `20260512191541_AddMeetingTasksTables`

   Without these, the implementation references compile errors on every type.

2. **PR target must be `feat/meeting-task-validation-epic`**, not `main`. The brief is explicit; the epic is still in-flight on its own branch.

3. **Confirm `MeetingTranscriptRepository.GetByIdAsync` eagerly loads `Tasks`** (Include). This is already validated by `GetTranscriptDetailHandler`, which iterates `transcript.Tasks` immediately after `GetByIdAsync`, so the eager-load path is exercised — no change required, but verify before declaring done.

No database migrations, no new DI registrations, no infrastructure changes are needed.