# Specification: Meeting Task Read Handlers — List & Detail

## Summary
Implement two MediatR read handlers (`GetTranscriptList` and `GetTranscriptDetail`) plus supporting DTOs for the Meeting Task Validation Checkpoint feature. The list handler returns paged, status-filterable transcript summaries; the detail handler returns a single transcript with all proposed tasks. Both handlers are pure read operations against `IMeetingTranscriptRepository`.

## Background
This is Subtask 3 of the **Meeting Task Validation Checkpoint** epic (`feat/meeting-task-validation-epic`). Earlier subtasks introduced the `MeetingTranscript` aggregate (with `PlaudRecordingId` and `PlaudCreatedAt` replacing the legacy `SourceEmail` field), the `ProposedTask` entity, the `IMeetingTranscriptRepository` interface, and the persistence layer. This subtask exposes that data to the application layer so that follow-up subtasks can wire up MVC controllers and the React review UI. The handlers must follow existing Vertical Slice conventions in `Anela.Heblo.Application/Features/*/UseCases/*` (MediatR `IRequest`/`IRequestHandler`, `BaseResponse` envelope, `ErrorCodes` enum).

## Functional Requirements

### FR-1: MeetingTranscriptDto
A DTO class exposing transcript metadata and rolled-up task counts to API consumers.

**Acceptance criteria:**
- Defined as a `class` (not `record`) per project DTO rule.
- Located at `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs`.
- Properties: `Id` (Guid), `PlaudRecordingId` (string), `PlaudCreatedAt` (DateTime), `Subject` (string), `Summary` (string), `Status` (string — enum name), `ReceivedAt` (DateTime), `ReviewedAt` (DateTime?), `ReviewedByUser` (string?), `TaskCount` (int), `ApprovedTaskCount` (int), `RejectedTaskCount` (int), `Tasks` (`List<ProposedTaskDto>`).
- `RawTranscript` is **not** exposed (kept internal to the domain entity).
- Non-nullable reference-type properties default to `null!` or empty collection initializer to satisfy nullable analysis without surfacing initialization warnings.

### FR-2: ProposedTaskDto
A DTO class exposing a proposed task's fields for display.

**Acceptance criteria:**
- Defined as a `class` (not `record`).
- Located at `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/ProposedTaskDto.cs`.
- Properties: `Id` (Guid), `Title` (string), `Description` (string), `Assignee` (string), `DueDate` (DateTime?), `Status` (string — enum name), `ExternalTaskId` (string?), `IsManuallyAdded` (bool).

### FR-3: GetTranscriptList Request
Query request for a paged, optionally status-filtered list of transcripts.

**Acceptance criteria:**
- `GetTranscriptListRequest : IRequest<GetTranscriptListResponse>` in `UseCases/GetTranscriptList/`.
- Properties: `StatusFilter` (string?, optional), `PageNumber` (int, default 1), `PageSize` (int, default 20).
- `StatusFilter` is matched against `MeetingTranscriptStatus` case-insensitively; unrecognized or empty values yield no filter (all statuses returned).

### FR-4: GetTranscriptList Response
Envelope carrying the page of transcript summaries.

**Acceptance criteria:**
- `GetTranscriptListResponse : BaseResponse`.
- Properties: `Items` (`List<MeetingTranscriptDto>`), `TotalCount` (int), `PageNumber` (int), `PageSize` (int), computed `TotalPages` (`Ceiling(TotalCount / PageSize)`).
- On success, `Items` is populated and `Success = true` (inherited default from `BaseResponse`).

### FR-5: GetTranscriptListHandler
Handler that fetches and maps the page.

**Acceptance criteria:**
- Constructor-injects `IMeetingTranscriptRepository`.
- Parses `StatusFilter` to nullable `MeetingTranscriptStatus` via `Enum.TryParse` (case-insensitive); on parse failure, passes `null` (no filter).
- Calls `_repository.GetListAsync(statusFilter, pageNumber, pageSize, cancellationToken)` and expects a `(items, totalCount)` tuple.
- Maps each `MeetingTranscript` to `MeetingTranscriptDto` with:
  - `Status` = `t.Status.ToString()`
  - `TaskCount` = `t.Tasks.Count`
  - `ApprovedTaskCount` = `t.Tasks.Count(x => x.Status == ProposedTaskStatus.Approved)`
  - `RejectedTaskCount` = `t.Tasks.Count(x => x.Status == ProposedTaskStatus.Rejected)`
  - `Tasks` left as an empty list (list view does not include task details — fetched via detail endpoint).
- Returns populated `GetTranscriptListResponse` with echoed paging parameters.

### FR-6: GetTranscriptDetail Request
Query request for a single transcript by Id.

**Acceptance criteria:**
- `GetTranscriptDetailRequest : IRequest<GetTranscriptDetailResponse>` in `UseCases/GetTranscriptDetail/`.
- Single property: `Id` (Guid).

### FR-7: GetTranscriptDetail Response
Envelope carrying a single transcript with tasks.

**Acceptance criteria:**
- `GetTranscriptDetailResponse : BaseResponse`.
- Single property: `Transcript` (`MeetingTranscriptDto`, nullable in practice when not found, `null!` initializer to align with project pattern).
- On not-found, `Success = false` and `ErrorCode = ErrorCodes.NotFound`; `Transcript` is not set.

### FR-8: GetTranscriptDetailHandler
Handler that fetches one transcript with its tasks.

**Acceptance criteria:**
- Constructor-injects `IMeetingTranscriptRepository`.
- Calls `_repository.GetByIdAsync(request.Id, cancellationToken)`.
- If repository returns `null`, returns `GetTranscriptDetailResponse { Success = false, ErrorCode = ErrorCodes.NotFound }`.
- Otherwise maps to `MeetingTranscriptDto` populating **all fields including the full `Tasks` list** (each as `ProposedTaskDto`).
- Aggregate counts (`TaskCount`, `ApprovedTaskCount`, `RejectedTaskCount`) are computed identically to the list handler.

### FR-9: Unit Tests — GetTranscriptListHandler
**Acceptance criteria:**
- File: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs`.
- Test: `Handle_ReturnsPagedResults` — given a single seeded transcript with one pending task, asserts `result.Success`, single item, mapped `Status == "PendingReview"`, `PlaudRecordingId == "rec-001"`, `TaskCount == 1`, `TotalCount == 1`.
- Repository is mocked with Moq; `GetListAsync` is set up to return the seeded tuple.

### FR-10: Unit Tests — GetTranscriptDetailHandler
**Acceptance criteria:**
- File: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptDetailHandlerTests.cs`.
- Test: `Handle_ExistingTranscript_ReturnsDetailWithTasks` — given a seeded transcript with one task, asserts `Success`, mapped `Subject`, mapped `PlaudRecordingId`, and `Tasks` list contains the task.
- Test: `Handle_NonExistentTranscript_ReturnsNotFound` — repository returns `null`; assert `Success == false` (and implicitly `ErrorCode == NotFound`).
- Repository is mocked with Moq.

### FR-11: Build and Test Validation
**Acceptance criteria:**
- `dotnet build` of the backend solution succeeds with no new warnings.
- `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GetTranscriptListHandlerTests|FullyQualifiedName~GetTranscriptDetailHandlerTests"` passes all tests.
- `dotnet format` produces no diff for new files.

### FR-12: Source Control
**Acceptance criteria:**
- Work is performed on the epic branch `feat/meeting-task-validation-epic` (or a short-lived feature branch off it; PR target = epic branch, **not** `main`).
- Single commit message: `feat(meeting-tasks): add GetTranscriptList and GetTranscriptDetail handlers with tests`.
- Staged paths limited to the four new directories/files listed in the brief.

## Non-Functional Requirements

### NFR-1: Performance
- List queries must be paged at the repository layer (no client-side paging).
- Default `PageSize = 20`; no enforced upper bound at handler level for this subtask (repository or future validator may add one).
- Task count rollups in the list view rely on `t.Tasks` already being loaded by the repository (eager include or projection); the handler does **not** issue additional queries per row.

### NFR-2: Security
- Handlers themselves perform no authorization — controller-level authorization (added in a later subtask) gates access.
- DTOs do not expose `RawTranscript` or any field containing PII beyond what the existing UI already shows (`Subject`, `Summary`, `Assignee` name string, `ReviewedByUser`).
- No user input is concatenated into queries; filter parsing is via strict `Enum.TryParse`.

### NFR-3: Maintainability / Style
- Follow existing Vertical Slice file layout (`UseCases/<UseCase>/{Request,Response,Handler}.cs`).
- DTOs as classes per project rule (OpenAPI generator constraint).
- Nullable reference types enabled; non-nullable properties initialized with `null!` or empty collections to match the codebase pattern.
- No comments unless a non-obvious invariant requires explanation.

## Data Model

Consumed entities (already defined by earlier subtasks):

- **MeetingTranscript** (aggregate root)
  - `Id : Guid`
  - `PlaudRecordingId : string` (replaces former `SourceEmail`)
  - `PlaudCreatedAt : DateTime`
  - `Subject : string`
  - `Summary : string`
  - `RawTranscript : string` (not exposed in DTO)
  - `Status : MeetingTranscriptStatus` (enum)
  - `ReceivedAt : DateTime`
  - `ReviewedAt : DateTime?`
  - `ReviewedByUser : string?`
  - `Tasks : List<ProposedTask>`

- **ProposedTask**
  - `Id : Guid`
  - `Title : string`
  - `Description : string`
  - `Assignee : string`
  - `DueDate : DateTime?`
  - `Status : ProposedTaskStatus` (enum: `Pending`, `Approved`, `Rejected`, …)
  - `ExternalTaskId : string?`
  - `IsManuallyAdded : bool`

- **MeetingTranscriptStatus** (enum) — values include at minimum `PendingReview`; full set defined by earlier subtask.

DTO mappings introduced here:
- `MeetingTranscript → MeetingTranscriptDto` (status as string; task list optionally populated)
- `ProposedTask → ProposedTaskDto`

## API / Interface Design

MediatR contracts (no HTTP controllers in this subtask):

**GetTranscriptList**
- Request: `{ StatusFilter?: string, PageNumber: int = 1, PageSize: int = 20 }`
- Response: `BaseResponse + { Items: MeetingTranscriptDto[], TotalCount, PageNumber, PageSize, TotalPages }`
- Items have empty `Tasks` arrays (summary view only).

**GetTranscriptDetail**
- Request: `{ Id: Guid }`
- Response (found): `BaseResponse(Success=true) + { Transcript: MeetingTranscriptDto with full Tasks }`
- Response (not found): `BaseResponse(Success=false, ErrorCode=NotFound)`

Repository contract used (already exists):
- `Task<(IReadOnlyList<MeetingTranscript> items, int totalCount)> GetListAsync(MeetingTranscriptStatus? status, int pageNumber, int pageSize, CancellationToken ct)`
- `Task<MeetingTranscript?> GetByIdAsync(Guid id, CancellationToken ct)`

## Dependencies

- **MediatR** — handler dispatch.
- **`Anela.Heblo.Application.Shared.BaseResponse` and `ErrorCodes`** — response envelope.
- **`Anela.Heblo.Domain.Features.MeetingTasks`** — `MeetingTranscript`, `ProposedTask`, `MeetingTranscriptStatus`, `ProposedTaskStatus`, `IMeetingTranscriptRepository` (all from prior epic subtasks; must be merged into the epic branch before this work starts).
- **Moq + xUnit** — already used in `Anela.Heblo.Tests`.
- **Epic branch** `feat/meeting-task-validation-epic` — must exist and contain Subtasks 1 & 2.

## Out of Scope

- MVC controllers / HTTP endpoints (later subtask).
- Authorization policies on the endpoints.
- Approve / Reject / manual-add task mutation handlers (later subtask).
- Frontend React review UI (later subtask).
- OpenAPI client regeneration (handled at controller subtask).
- Additional list filters (date range, assignee search, full-text) beyond `StatusFilter`.
- Repository implementation changes — repository is consumed as-is.
- Database migrations.

## Open Questions

None.

## Status: COMPLETE