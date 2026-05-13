# Specification: Meeting Task Write Handlers — Edit, Approve/Reject, Add Task

## Summary
Implement three MediatR write-side handlers for the Meeting Task Validation Checkpoint feature: `UpdateProposedTask` (edit task fields), `UpdateProposedTaskStatus` (approve/reject lifecycle), and `AddProposedTask` (manually create a task on an existing transcript). Each handler mutates a `MeetingTranscript` aggregate via `IMeetingTranscriptRepository`, persists via `SaveChangesAsync`, and returns a `BaseResponse`-shaped result with standardized error codes.

## Background
The parent epic (`feat/meeting-task-validation-epic`) introduces a human-in-the-loop validation step for AI-extracted meeting tasks. Earlier subtasks delivered the domain model (`MeetingTranscript`, `ProposedTask`, `ProposedTaskStatus`), repository contract (`IMeetingTranscriptRepository`), read-side queries, and the `ProposedTaskDto` contract. This subtask adds the write-side: handlers that let users edit a proposed task's fields, transition its status (Approved/Rejected), or insert an additional manually-authored task into the transcript's task list. These handlers will later be exposed via MVC controller endpoints and consumed by the frontend validation UI.

## Functional Requirements

### FR-1: UpdateProposedTask handler
Allow editing the editable fields of an existing proposed task on a transcript.

**Request shape (`UpdateProposedTaskRequest : IRequest<BaseResponse>`):**
- `TranscriptId: Guid`
- `TaskId: Guid`
- `Title: string` (required, `[Required]`)
- `Description: string` (defaults to empty string)
- `Assignee: string` (required, `[Required]`)
- `DueDate: DateTime?` (optional)

**Behavior:**
1. Load the `MeetingTranscript` by `TranscriptId` via `IMeetingTranscriptRepository.GetByIdAsync`.
2. If the transcript is not found, return `Success = false`, `ErrorCode = ErrorCodes.NotFound`.
3. Locate the `ProposedTask` with `Id == TaskId` inside `transcript.Tasks`.
4. If the task is not found, return `Success = false`, `ErrorCode = ErrorCodes.NotFound`.
5. Overwrite `Title`, `Description`, `Assignee`, `DueDate` on the task entity (in-place — EF tracks the loaded aggregate).
6. Call `SaveChangesAsync(cancellationToken)` on the repository.
7. Return `new UpdateProposedTaskResponse()` (defaults to `Success = true`).

**Response type:** `UpdateProposedTaskResponse : BaseResponse` (empty marker class — preserves a strongly typed handler return for future fields).

**Acceptance criteria:**
- Updating an existing task on an existing transcript mutates the four target fields and persists.
- Missing transcript → response carries `Success = false` and `ErrorCode = ErrorCodes.NotFound`.
- Missing task on an existing transcript → response carries `Success = false` and `ErrorCode = ErrorCodes.NotFound`.
- `Title` and `Assignee` enforce `[Required]` validation at the MVC binding layer.
- Unit test `UpdateTask_ModifiesFields` passes.

### FR-2: UpdateProposedTaskStatus handler
Transition a proposed task's lifecycle status (e.g., Pending → Approved/Rejected).

**Request shape (`UpdateProposedTaskStatusRequest : IRequest<BaseResponse>`):**
- `TranscriptId: Guid`
- `TaskId: Guid`
- `Status: string` (required, `[Required]`) — string form of `ProposedTaskStatus` enum, case-insensitive

**Behavior:**
1. Load transcript by id. Missing → `Success = false`, `ErrorCode = ErrorCodes.NotFound`.
2. Locate task by id. Missing → `Success = false`, `ErrorCode = ErrorCodes.NotFound`.
3. Parse `request.Status` with `Enum.TryParse<ProposedTaskStatus>(value, ignoreCase: true, out var newStatus)`. Failure → `Success = false`, `ErrorCode = ErrorCodes.ValidationError`.
4. Assign `task.Status = newStatus`.
5. Call `SaveChangesAsync(cancellationToken)`.
6. Return `new UpdateProposedTaskStatusResponse()` on success.

**Response type:** `UpdateProposedTaskStatusResponse : BaseResponse` (empty marker class).

**Acceptance criteria:**
- A valid status string (e.g., `"Approved"`, `"approved"`, `"REJECTED"`) parses and is applied to the task.
- An unrecognized status string returns `ErrorCode = ErrorCodes.ValidationError` and the task is unchanged.
- Missing transcript or task returns `ErrorCode = ErrorCodes.NotFound`.
- Unit test `UpdateTaskStatus_ApprovesTask` passes.

### FR-3: AddProposedTask handler
Create a new manually-authored task and append it to an existing transcript's task list.

**Request shape (`AddProposedTaskRequest : IRequest<AddProposedTaskResponse>`):**
- `TranscriptId: Guid`
- `Title: string` (required, `[Required]`)
- `Description: string` (defaults to empty)
- `Assignee: string` (required, `[Required]`)
- `DueDate: DateTime?` (optional)

**Behavior:**
1. Load transcript by id. Missing → `Success = false`, `ErrorCode = ErrorCodes.NotFound`, `Task = null`.
2. Construct a new `ProposedTask`:
   - `Id = Guid.NewGuid()`
   - `MeetingTranscriptId = transcript.Id`
   - `Title`, `Description`, `Assignee`, `DueDate` from the request
   - `Status = ProposedTaskStatus.Pending`
   - `IsManuallyAdded = true`
3. Append to `transcript.Tasks`.
4. Call `SaveChangesAsync(cancellationToken)`.
5. Return `AddProposedTaskResponse` with a populated `ProposedTaskDto`:
   - `Id, Title, Description, Assignee, DueDate` mirror the created entity
   - `Status = task.Status.ToString()` (string form for transport)
   - `IsManuallyAdded = true`

**Response type:** `AddProposedTaskResponse : BaseResponse { public ProposedTaskDto Task { get; set; } }`.

**Acceptance criteria:**
- On a valid transcript, a new task is appended with `IsManuallyAdded = true` and `Status = Pending`.
- The response `Task` DTO reflects the persisted entity.
- Missing transcript returns `Success = false`, `ErrorCode = ErrorCodes.NotFound`.
- Unit test `AddTask_CreatesManualTask` passes.

### FR-4: Unit tests
A single test class `UpdateProposedTaskHandlerTests` covers happy paths for all three handlers using a Moq `IMeetingTranscriptRepository`.

**Acceptance criteria:**
- Test file location: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs`.
- Tests included: `UpdateTask_ModifiesFields`, `UpdateTaskStatus_ApprovesTask`, `AddTask_CreatesManualTask`.
- Helper `CreateTranscriptWithTask(out Guid transcriptId, out Guid taskId)` constructs a transcript with `Status = MeetingTranscriptStatus.PendingReview`, one seeded `ProposedTask` (`Status = Pending`), and required scalar fields (`Subject`, `Summary`, `SourceEmail`, `ReceivedAt`).
- `dotnet test --filter "FullyQualifiedName~UpdateProposedTaskHandlerTests"` reports 3 passing tests.

### FR-5: File layout
Each handler lives in its own UseCase folder following Vertical Slice conventions.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskHandler.cs`
- Namespaces match folder structure (`Anela.Heblo.Application.Features.MeetingTasks.UseCases.{UseCase}`).

## Non-Functional Requirements

### NFR-1: Performance
Each handler performs a single aggregate load + single `SaveChangesAsync`. No batched, looped, or N+1 access. Repository calls accept the request's `CancellationToken`. Targeted single-handler call should complete in <200ms against a warm database for typical transcripts (≤50 proposed tasks).

### NFR-2: Security
- Handlers themselves do not enforce authorization — that responsibility belongs to the controller layer (out of scope for this subtask). However, handlers must not return data beyond what the caller is authorized to mutate; since input is keyed by `TranscriptId`/`TaskId` and only mutates fields on the located task, no data leakage occurs.
- `[Required]` validation on `Title`, `Assignee`, and `Status` runs at MVC model binding.
- No raw SQL — repository abstracts EF Core access, eliminating injection risk.

### NFR-3: Reliability
- All three handlers must return a `BaseResponse`-shaped result instead of throwing for known business failures (not found, validation). Unexpected exceptions propagate.
- `CancellationToken` from MediatR is honored on every repository call.

### NFR-4: Consistency
- Error codes follow the existing `ErrorCodes` static class (`NotFound`, `ValidationError`) already used by sibling MeetingTasks handlers.
- Response DTOs inherit `BaseResponse`; success defaults to `true`, failure paths set `Success = false` and `ErrorCode`.

### NFR-5: Testability
- All collaborators are injected via constructor (`IMeetingTranscriptRepository`).
- Tests use Moq with no real EF or database.
- Tests assert both the handler's return value and the mutation on the in-memory aggregate.

## Data Model

No schema changes. Handlers operate on existing entities introduced by earlier subtasks of the epic:

- **`MeetingTranscript`** (aggregate root): `Id`, `Subject`, `Summary`, `SourceEmail`, `Status: MeetingTranscriptStatus`, `ReceivedAt`, `Tasks: ICollection<ProposedTask>`.
- **`ProposedTask`** (child entity): `Id`, `MeetingTranscriptId`, `Title`, `Description`, `Assignee`, `DueDate: DateTime?`, `Status: ProposedTaskStatus`, `IsManuallyAdded: bool`.
- **`ProposedTaskStatus`** (enum): `Pending`, `Approved`, `Rejected` (exact members defined by prior subtask).
- **`MeetingTranscriptStatus`** (enum): includes at least `PendingReview`.
- **`ProposedTaskDto`** (contract): `Id`, `Title`, `Description`, `Assignee`, `DueDate`, `Status: string`, `IsManuallyAdded: bool`.

`AddProposedTask` mutates the `Tasks` collection (insert). `UpdateProposedTask` mutates four scalar fields. `UpdateProposedTaskStatus` mutates `Status`. All changes persist through EF Core change tracking via `SaveChangesAsync`.

## API / Interface Design

MediatR request/response contracts (no controller wiring in this subtask):

| Request | Response | Mutation |
|---|---|---|
| `UpdateProposedTaskRequest` | `UpdateProposedTaskResponse : BaseResponse` | Updates `Title`, `Description`, `Assignee`, `DueDate` on a task |
| `UpdateProposedTaskStatusRequest` | `UpdateProposedTaskStatusResponse : BaseResponse` | Updates `Status` on a task (string-parsed to enum) |
| `AddProposedTaskRequest` | `AddProposedTaskResponse : BaseResponse { ProposedTaskDto Task }` | Appends a new task with `IsManuallyAdded = true`, `Status = Pending` |

Dispatched via `IMediator.Send(request, cancellationToken)`. Error signaling: never throw for known failures; populate `Success = false` and `ErrorCode` on the response. Successful response defaults to `Success = true`.

## Dependencies

- **MediatR** — `IRequest<TResponse>`, `IRequestHandler<TRequest, TResponse>`.
- **`IMeetingTranscriptRepository`** (`Anela.Heblo.Domain.Features.MeetingTasks`) — `GetByIdAsync(Guid, CancellationToken)` and `SaveChangesAsync(CancellationToken)`. Must already expose these signatures from a prior epic subtask.
- **`MeetingTranscript`, `ProposedTask`, `ProposedTaskStatus`** domain types (`Anela.Heblo.Domain.Features.MeetingTasks`).
- **`ProposedTaskDto`** (`Anela.Heblo.Application.Features.MeetingTasks.Contracts`).
- **`BaseResponse`, `ErrorCodes`** (`Anela.Heblo.Application.Shared`).
- **`System.ComponentModel.DataAnnotations`** for `[Required]`.
- **Test packages:** `xUnit`, `Moq`.
- **Branching:** feature branch must be created from `feat/meeting-task-validation-epic`; PR targets `feat/meeting-task-validation-epic` (not `main`).

## Out of Scope

- MVC controller endpoints exposing these handlers (separate subtask).
- Authorization/authentication checks (handled at controller layer).
- Frontend UI for editing/approving/adding tasks.
- Status transition rules (e.g., disallowing Approved→Pending) — current handler accepts any valid enum value.
- Domain events, audit logging, or notifications on status change.
- Validation that the transcript is in `PendingReview` state before edits — not specified in brief.
- Bulk approve/reject across multiple tasks.
- Optimistic concurrency / `RowVersion` handling.
- E2E tests for these handlers; only unit tests are required here.
- OpenAPI/Swagger DTO surface — there is no controller in this subtask.

## Open Questions

None.

## Status: COMPLETE