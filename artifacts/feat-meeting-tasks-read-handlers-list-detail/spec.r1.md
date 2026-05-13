# Specification: Meeting Tasks Read Handlers — List & Detail

## Summary
Implement two MediatR read handlers (`GetTranscriptList` and `GetTranscriptDetail`) plus their DTOs for the Meeting Task Validation Checkpoint epic. The list handler returns paginated transcript summaries with task counts; the detail handler returns a single transcript with all proposed tasks. Both are read-only operations backed by `IMeetingTranscriptRepository`.

## Background
This is **Subtask 3** of the Meeting Task Validation Checkpoint epic (parent branch `feat/meeting-task-validation-epic`). Prior subtasks established the domain entities (`MeetingTranscript`, `ProposedTask`), the persistence layer, and the `IMeetingTranscriptRepository` abstraction. The read handlers feed the UI surfaces where a reviewer browses pending meeting transcripts and inspects the AI-proposed tasks before approving/rejecting them.

A recent epic-level change replaced `SourceEmail` with `PlaudRecordingId` (Plaud is the recording device/service) and added `PlaudCreatedAt` for display in the UI. DTOs and handlers reflect that change.

## Functional Requirements

### FR-1: MeetingTranscriptDto contract
Add a class DTO `MeetingTranscriptDto` in `Anela.Heblo.Application/Features/MeetingTasks/Contracts/`.

Fields:
- `Guid Id`
- `string PlaudRecordingId` (non-null)
- `DateTime PlaudCreatedAt`
- `string Subject` (non-null)
- `string Summary` (non-null)
- `string Status` (string form of `MeetingTranscriptStatus` enum)
- `DateTime ReceivedAt`
- `DateTime? ReviewedAt`
- `string? ReviewedByUser`
- `int TaskCount`
- `int ApprovedTaskCount`
- `int RejectedTaskCount`
- `List<ProposedTaskDto> Tasks` (initialized to empty list)

**Acceptance criteria:**
- Class type (not C# record), per project rule on OpenAPI-generated DTOs.
- `RawTranscript` is NOT exposed in the list DTO (large field; detail endpoint also excludes it for this subtask).
- All non-nullable reference types use `= null!;` initializers to match codebase convention.

### FR-2: ProposedTaskDto contract
Add a class DTO `ProposedTaskDto` in `Anela.Heblo.Application/Features/MeetingTasks/Contracts/`.

Fields:
- `Guid Id`
- `string Title` (non-null)
- `string Description` (non-null)
- `string Assignee` (non-null)
- `DateTime? DueDate`
- `string Status` (string form of `ProposedTaskStatus` enum)
- `string? ExternalTaskId`
- `bool IsManuallyAdded`

**Acceptance criteria:**
- Class type (not C# record).
- Field set matches the brief exactly.

### FR-3: GetTranscriptListHandler
Implement MediatR request/response/handler trio under `UseCases/GetTranscriptList/`.

Request inputs:
- `string? StatusFilter` — case-insensitive parse against `MeetingTranscriptStatus`; invalid or empty values mean "no filter".
- `int PageNumber` (default 1)
- `int PageSize` (default 20)

Response:
- Inherits from `BaseResponse` (Success/ErrorCode pattern).
- `List<MeetingTranscriptDto> Items` — list view, with `Tasks` field left empty (counts only).
- `int TotalCount`, `int PageNumber`, `int PageSize`, computed `int TotalPages`.

Handler behavior:
- Parse the optional `StatusFilter` to `MeetingTranscriptStatus?`.
- Call `_repository.GetListAsync(statusFilter, pageNumber, pageSize, ct)` which returns `(IEnumerable<MeetingTranscript>, int totalCount)`.
- Project each transcript into a `MeetingTranscriptDto` with `Tasks = new()` (empty) and populated counts.
- Status is serialized via `.ToString()` on the enum.

**Acceptance criteria:**
- Unit test `Handle_ReturnsPagedResults` passes: single transcript returned, status string is `"PendingReview"`, `PlaudRecordingId` echoed, `TaskCount == 1`, `TotalCount == 1`.
- `result.Success` is `true` (inherited default).
- Empty/invalid `StatusFilter` is treated as no filter (passed as `null` to the repository).
- Handler returns even if the page is empty (`Items = []`, `TotalCount = 0`).

### FR-4: GetTranscriptDetailHandler
Implement MediatR request/response/handler trio under `UseCases/GetTranscriptDetail/`.

Request:
- `Guid Id` — transcript identifier.

Response:
- Inherits from `BaseResponse`.
- `MeetingTranscriptDto Transcript` (non-null on success path).

Handler behavior:
- Call `_repository.GetByIdAsync(request.Id, ct)`.
- If `null`, return a failure response with `Success = false` and `ErrorCode = ErrorCodes.NotFound`. Do not throw.
- Otherwise, project to `MeetingTranscriptDto` with the full `Tasks` list populated (each as `ProposedTaskDto`).

**Acceptance criteria:**
- Unit test `Handle_ExistingTranscript_ReturnsDetailWithTasks` passes: `Success == true`, `Subject == "Sprint Planning"`, `PlaudRecordingId == "rec-001"`, exactly one task in the returned DTO.
- Unit test `Handle_NonExistentTranscript_ReturnsNotFound` passes: `Success == false`.
- `Tasks` is populated in detail (in contrast to the list handler).

### FR-5: Unit tests
Create two test files under `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/`:
- `GetTranscriptListHandlerTests.cs` with `Handle_ReturnsPagedResults`.
- `GetTranscriptDetailHandlerTests.cs` with `Handle_ExistingTranscript_ReturnsDetailWithTasks` and `Handle_NonExistentTranscript_ReturnsNotFound`.

**Acceptance criteria:**
- Uses Moq for `IMeetingTranscriptRepository`.
- xUnit `[Fact]` test methods, AAA structure.
- All three tests pass under `dotnet test --filter "FullyQualifiedName~GetTranscriptListHandlerTests|FullyQualifiedName~GetTranscriptDetailHandlerTests"`.

## Non-Functional Requirements

### NFR-1: Performance
- Pagination is repository-side (no in-memory paging). Default page size 20, no enforced upper bound in this subtask (validation will arrive with the controller layer).
- List projection does not load `Tasks` collections beyond counts. The implementation depends on the repository materialising tasks for count operations; if N+1 emerges, the repository — not the handler — owns the fix.

### NFR-2: Security
- Handlers themselves perform no authorization; the MVC controller layer (out of scope here) enforces authentication and role checks.
- No PII transformations or redaction in this layer.

### NFR-3: Architectural conformance
- Vertical-slice layout under `Features/MeetingTasks/UseCases/{UseCase}/`.
- MediatR `IRequest<TResponse>` + `IRequestHandler<TRequest, TResponse>` pattern.
- DTOs are classes, not records (OpenAPI generator compatibility — project-wide rule).
- Responses inherit from `Anela.Heblo.Application.Shared.BaseResponse`.
- Constructor injection only; no service locator.

### NFR-4: Validation
- BE validation gate: `dotnet build` + `dotnet format` both clean.
- All new tests pass; previously passing tests remain green.

## Data Model
Read-only consumption of existing entities (established in earlier subtasks):

- **MeetingTranscript** — aggregate root.
  - `Id: Guid`, `PlaudRecordingId: string`, `PlaudCreatedAt: DateTime`, `Subject: string`, `Summary: string`, `RawTranscript: string`, `Status: MeetingTranscriptStatus`, `ReceivedAt: DateTime`, `ReviewedAt: DateTime?`, `ReviewedByUser: string?`, `Tasks: ICollection<ProposedTask>`.
- **ProposedTask** — child entity.
  - `Id: Guid`, `Title: string`, `Description: string`, `Assignee: string`, `DueDate: DateTime?`, `Status: ProposedTaskStatus`, `ExternalTaskId: string?`, `IsManuallyAdded: bool`.
- **Enums**
  - `MeetingTranscriptStatus` — includes `PendingReview` (other states defined by earlier subtasks).
  - `ProposedTaskStatus` — includes `Pending`, `Approved`, `Rejected`.

## API / Interface Design

MediatR contracts only (HTTP controllers are out of scope here):

**GetTranscriptList**
- Request: `GetTranscriptListRequest { string? StatusFilter; int PageNumber = 1; int PageSize = 20; }`
- Response: `GetTranscriptListResponse : BaseResponse { List<MeetingTranscriptDto> Items; int TotalCount; int PageNumber; int PageSize; int TotalPages; }`

**GetTranscriptDetail**
- Request: `GetTranscriptDetailRequest { Guid Id; }`
- Response: `GetTranscriptDetailResponse : BaseResponse { MeetingTranscriptDto Transcript; }`
- Not-found path: `Success = false`, `ErrorCode = ErrorCodes.NotFound`, `Transcript` left at its default.

Status string values returned via `Enum.ToString()` (e.g., `"PendingReview"`, `"Approved"`).

## Dependencies
- **MediatR** — already referenced by the Application project.
- **IMeetingTranscriptRepository** with methods `GetListAsync(MeetingTranscriptStatus? statusFilter, int pageNumber, int pageSize, CancellationToken) → (IEnumerable<MeetingTranscript>, int)` and `GetByIdAsync(Guid id, CancellationToken) → MeetingTranscript?`. Created in an earlier subtask of this epic.
- **`Anela.Heblo.Application.Shared.BaseResponse`** and **`ErrorCodes.NotFound`** — existing shared error contract.
- **Moq + xUnit** — existing test stack.

## Out of Scope
- MVC controller endpoints exposing these handlers over HTTP.
- Authorization and authentication wiring.
- Frontend / OpenAPI TypeScript client regeneration.
- Mutation handlers (Approve/Reject/AddManualTask) — separate subtasks.
- Repository implementation changes — assumed complete from prior subtasks.
- Integration tests against the database; only unit tests with Moq are required here.
- Returning `RawTranscript` to clients (deferred to a future subtask that needs it).
- Pagination boundary validation (e.g., clamping page size) — will be added at the controller layer.

## Open Questions
None.

## Status: COMPLETE