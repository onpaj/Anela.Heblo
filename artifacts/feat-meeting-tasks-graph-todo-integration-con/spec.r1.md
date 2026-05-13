# Specification: Graph TODO Integration & Controller Wiring

## Summary
Integrate approved meeting tasks with Microsoft TODO by resolving assignees to Graph user IDs, creating tasks in a configured shared list, and exposing the full meeting-task review workflow through a REST API. This is Subtask 5 of the Meeting Task Validation Checkpoint epic and wires together the previously built use cases under a single authenticated controller.

## Background
The Meeting Task Validation Checkpoint epic ingests Plaud-recorded meeting transcripts, extracts proposed tasks via Claude, and surfaces them in a review UI. After human review, approved tasks must be pushed into each assignee's Microsoft TODO list so the assignee sees them in their normal task surface (Outlook, Teams, TODO app). This subtask delivers (a) the Graph API client that resolves users and creates TODO tasks, (b) the MediatR handler that orchestrates submission across all approved tasks of a transcript, and (c) the controller, options, and DI module that expose the full feature externally. The n8n webhook endpoint is intentionally omitted — ingestion is handled by `PlaudPollingJob` (issue #647).

## Functional Requirements

### FR-1: Graph user resolution
Resolve a free-text assignee display name (as extracted from the transcript by Claude) to a Microsoft Graph user ID using the `/users?$filter=displayName eq '...'` query.

**Acceptance criteria:**
- `IGraphTodoService.ResolveUserIdAsync(string assigneeName, CancellationToken)` returns the matching user's `id` when exactly one user matches by `displayName`.
- Returns `null` when no user matches.
- Returns the first match when multiple users share the display name (documented limitation; see Open Questions).
- The display name is URI-escaped before being placed in the OData filter to avoid injection or malformed query errors.
- Failures (network, 4xx/5xx, deserialization) are logged at Warning level and surface as `null` (non-throwing) so the caller can record per-task errors rather than aborting the entire submission.
- Authentication uses an app-only token from `ITokenAcquisition.GetAccessTokenForAppAsync(GraphScope)` with scope `https://graph.microsoft.com/.default`.

### FR-2: Microsoft TODO task creation
Create a task in the configured shared TODO list under the resolved user's account.

**Acceptance criteria:**
- `IGraphTodoService.CreateTodoTaskAsync(userId, title, description, dueDate, ct)` returns `TodoTaskResult(Success=true, ExternalTaskId=<graph id>, Error=null)` on success.
- The service first calls `GET /users/{userId}/todo/lists` and finds the list whose `displayName` matches `MeetingTasksOptions.TodoListName` case-insensitively. If absent, it creates the list via `POST /users/{userId}/todo/lists`.
- Task body uses Graph schema: `title`, `body` (`contentType=text`), and optional `dueDateTime` (`{ dateTime, timeZone: "UTC" }`).
- A non-success HTTP response, exception, or deserialization failure yields `TodoTaskResult(Success=false, ExternalTaskId=null, Error=<exception message>)` and logs at Error level.
- The task creation POST hits `/users/{userId}/todo/lists/{listId}/tasks` and the returned `id` is captured as `ExternalTaskId`.
- Uses the same app-only Graph token as FR-1.

### FR-3: Submit approved tasks for a transcript
Expose a use case that pushes all approved-but-not-yet-submitted tasks of a transcript to Microsoft TODO and reports per-task success/failure.

**Acceptance criteria:**
- `SubmitToTodoRequest { TranscriptId }` returns `SubmitToTodoResponse { Success, SuccessCount, FailedCount, Errors[] }` (inherits the standard `BaseResponse`).
- Handler returns `Success=false` with `ErrorCode=NotFound` when the transcript does not exist.
- Handler selects only tasks where `Status == Approved && ExternalTaskId == null` for submission. Tasks already submitted are not re-sent.
- Tasks with `Status == Rejected` or `Status == Pending` are skipped (not submitted, not counted as failures).
- For each selected task: resolve assignee → if null, append to `Errors` and increment `FailedCount`; otherwise create the TODO task. On success, persist `task.ExternalTaskId`. On failure, append a descriptive entry to `Errors`.
- After processing, recompute transcript status:
  - All non-rejected tasks now have `ExternalTaskId != null` AND at least one task is `Rejected` → `PartiallyApproved`.
  - All non-rejected tasks now have `ExternalTaskId != null` AND no rejected tasks → `Approved`.
  - Otherwise → leave as `PendingReview`.
- Set `transcript.ReviewedAt = DateTime.UtcNow` and persist via `IMeetingTranscriptRepository.SaveChangesAsync`.
- Logs an Information-level summary `"Submitted {SuccessCount} tasks to TODO for transcript {Id}, {FailedCount} failed"`.

### FR-4: MeetingTasks DI module
Register all meeting-task services, repositories, jobs, and options in a single composition root extension method.

**Acceptance criteria:**
- `MeetingTasksModule.AddMeetingTasksModule(IServiceCollection, IConfiguration)` registers:
  - `IOptions<MeetingTasksOptions>` bound to configuration section `"MeetingTasks"`.
  - `IMeetingTranscriptRepository` → `MeetingTranscriptRepository` (Scoped).
  - `IMeetingTaskExtractor` → `ClaudeMeetingTaskExtractor` (Scoped).
  - `PlaudPollingJob` (Transient).
  - `IGraphTodoService` → factory that constructs `GraphTodoService` with the resolved `TodoListName` from options (Scoped).
- Module is invoked from `ApplicationModule.cs` directly after `services.AddMarketingInvoicesModule();` (around line 72).
- A new `using Anela.Heblo.Application.Features.MeetingTasks;` is added to `ApplicationModule.cs`.

### FR-5: MeetingTasksOptions
Provide strongly-typed configuration for the meeting-tasks feature.

**Acceptance criteria:**
- `MeetingTasksOptions` has property `TodoListName` (string) with default `"Meeting Actions"`.
- Bound from the `"MeetingTasks"` configuration section.

### FR-6: MeetingTasksController REST surface
Expose the full review workflow as authenticated REST endpoints under `/api/meeting-tasks`.

**Acceptance criteria:**
- Controller class-level `[Authorize]` requires authenticated user; no anonymous/webhook endpoints are present.
- Endpoints:
  | Method | Route | Request | Response | Notes |
  |--------|-------|---------|----------|-------|
  | GET    | `/api/meeting-tasks`                                              | `[FromQuery] GetTranscriptListRequest` | `GetTranscriptListResponse` | List transcripts |
  | GET    | `/api/meeting-tasks/{id:guid}`                                    | route id                               | `GetTranscriptDetailResponse` | Detail + tasks |
  | PUT    | `/api/meeting-tasks/{transcriptId:guid}/tasks/{taskId:guid}`      | `[FromBody] UpdateProposedTaskRequest` | base                          | Edit a proposed task |
  | PUT    | `/api/meeting-tasks/{transcriptId:guid}/tasks/{taskId:guid}/status` | `[FromBody] UpdateProposedTaskStatusRequest` | base                  | Approve/reject |
  | POST   | `/api/meeting-tasks/{transcriptId:guid}/tasks`                    | `[FromBody] AddProposedTaskRequest`    | `AddProposedTaskResponse`     | Manually add task |
  | POST   | `/api/meeting-tasks/{transcriptId:guid}/submit`                   | route id only                          | `SubmitToTodoResponse`        | Push approved → TODO |
- Route-bound fields (`TranscriptId`, `TaskId`) are assigned onto the body request before dispatch to MediatR.
- Controller inherits from `BaseApiController` and routes the MediatR response through `HandleResponse(...)`.
- No `[ApiKeyAuth]` attribute is created or used; the n8n webhook endpoint is explicitly absent.

## Non-Functional Requirements

### NFR-1: Performance
- Submission of a transcript with up to 20 approved tasks should complete within 30 seconds end-to-end under normal Graph latency. Tasks are submitted sequentially (per-task) to keep error attribution simple; parallelism is out of scope.
- A single per-user list lookup is performed per task creation. (Optimization: cache `userId → listId` per request is a future concern; see Open Questions.)

### NFR-2: Security
- All controller endpoints require an authenticated session via `[Authorize]`.
- Graph access uses an app-only token (client credentials flow) acquired through `ITokenAcquisition.GetAccessTokenForAppAsync`. The app registration must hold `Tasks.ReadWrite.All` and `User.Read.All` application permissions.
- Display names are URI-escaped before interpolation into OData filters (FR-1).
- Errors surfaced to the API client (`Errors[]` strings) include task title and assignee name but no token, user IDs, or stack traces.

### NFR-3: Reliability
- A single failed task does not abort processing of the rest of the batch.
- An exception in `GraphTodoService.CreateTodoTaskAsync` is captured and returned as `TodoTaskResult(false, …)`.
- Re-running `SubmitToTodo` on a transcript is safe: tasks with `ExternalTaskId != null` are skipped (idempotency at the task level).

### NFR-4: Testability
- Both `GraphTodoService` and `SubmitToTodoHandler` are unit-tested with mocked `IHttpClientFactory`/`ITokenAcquisition` and mocked `IGraphTodoService`/`IMeetingTranscriptRepository` respectively.
- All new tests must pass: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GraphTodoServiceTests"` and `…SubmitToTodoHandlerTests`.

### NFR-5: Build & Format
- `dotnet build backend/src/Anela.Heblo.API/` must succeed.
- `dotnet format` must report no diffs on new files.

## Data Model

The feature does not introduce new persisted entities. It mutates existing entities defined in earlier subtasks:

- **`MeetingTranscript`** (existing): fields read/written by this subtask:
  - `Status: MeetingTranscriptStatus` — transitions to `Approved` or `PartiallyApproved` on full processing.
  - `ReviewedAt: DateTime?` — stamped with `UtcNow` after submission.
  - `Tasks: List<ProposedTask>` — navigation collection iterated for approved tasks.
- **`ProposedTask`** (existing): fields read/written:
  - `Title`, `Description`, `Assignee`, `DueDate`, `Status` — read.
  - `ExternalTaskId: string?` — set to the Graph TODO task `id` on successful submission.
- **Enums** (existing): `MeetingTranscriptStatus { PendingReview, PartiallyApproved, Approved, … }`; `ProposedTaskStatus { Pending, Approved, Rejected }`.

External (Graph) entities consumed but not persisted: `user`, `todoTaskList`, `todoTask`.

## API / Interface Design

### Internal interfaces

```csharp
public record TodoTaskResult(bool Success, string? ExternalTaskId, string? Error);

public interface IGraphTodoService
{
    Task<string?> ResolveUserIdAsync(string assigneeName, CancellationToken ct = default);
    Task<TodoTaskResult> CreateTodoTaskAsync(
        string userId, string title, string description, DateTime? dueDate, CancellationToken ct = default);
}
```

### MediatR contracts

```csharp
public class SubmitToTodoRequest : IRequest<SubmitToTodoResponse>
{
    public Guid TranscriptId { get; set; }
}

public class SubmitToTodoResponse : BaseResponse
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = new();
}
```

### HTTP endpoints
See FR-6 table. All requests/responses are JSON; routes use `:guid` constraints; controller-level `[Authorize]` applies cookie/Entra ID auth as configured elsewhere in the API project.

### Outbound Graph calls
- `GET https://graph.microsoft.com/v1.0/users?$filter=displayName eq '{escaped}'&$select=id,displayName`
- `GET https://graph.microsoft.com/v1.0/users/{userId}/todo/lists`
- `POST https://graph.microsoft.com/v1.0/users/{userId}/todo/lists` body `{ "displayName": "<TodoListName>" }`
- `POST https://graph.microsoft.com/v1.0/users/{userId}/todo/lists/{listId}/tasks` body `{ title, body:{ contentType:"text", content }, dueDateTime?:{ dateTime, timeZone:"UTC" } }`

All requests use the `MicrosoftGraph` named `HttpClient` and include `Authorization: Bearer <app-token>`.

### Configuration (appsettings.json)
```json
"MeetingTasks": {
  "TodoListName": "Meeting Actions"
}
```

## Dependencies

**Existing in the codebase (assumed delivered by prior subtasks):**
- `Anela.Heblo.Domain.Features.MeetingTasks.MeetingTranscript`, `ProposedTask`, `MeetingTranscriptStatus`, `ProposedTaskStatus`, `IMeetingTranscriptRepository`.
- `Anela.Heblo.Persistence.MeetingTasks.MeetingTranscriptRepository`.
- `Anela.Heblo.Application.Features.MeetingTasks.Services.IMeetingTaskExtractor`, `ClaudeMeetingTaskExtractor`.
- `Anela.Heblo.Application.Features.MeetingTasks.Infrastructure.Jobs.PlaudPollingJob`.
- Use case requests/responses: `GetTranscriptList*`, `GetTranscriptDetail*`, `UpdateProposedTask*`, `UpdateProposedTaskStatus*`, `AddProposedTask*`.
- `Anela.Heblo.Application.Features.KnowledgeBase.Services.GraphApiHelpers` (reused for `GraphBaseUrl`, `CreateRequest`, `DeserializeAsync`).
- `Anela.Heblo.Application.Shared.BaseResponse` and `Shared.ErrorCodes.NotFound`.
- `BaseApiController.HandleResponse` and the API project's existing `[Authorize]` setup.

**External:**
- Microsoft Graph v1.0 (`/users`, `/users/{id}/todo/*`).
- `Microsoft.Identity.Abstractions.ITokenAcquisition` (already registered for app-only Graph access elsewhere).
- Named `HttpClient` `"MicrosoftGraph"` (must already be registered in HTTP client factory).
- Required Graph app permissions: `Tasks.ReadWrite.All`, `User.Read.All` (admin-consented).
- NuGet: existing `MediatR`, `Microsoft.Identity.Web`, `System.Net.Http`, `System.Text.Json`.

**Test dependencies:**
- `xunit`, `Moq`, `Moq.Protected` (already in the test project).

**Git/branch:**
- Branch is created from `feat/meeting-task-validation-epic`; PR targets `feat/meeting-task-validation-epic`, not `main`.

## Out of Scope
- n8n webhook ingestion endpoint and `ApiKeyAuthAttribute` (removed; replaced by `PlaudPollingJob`, issue #647).
- UI / frontend changes — this subtask is backend-only.
- Bulk approval/rejection endpoints — single-task status updates only (already delivered by `UpdateProposedTaskStatus`).
- Retry of failed TODO creations (caller may simply re-invoke `/submit`; idempotency relies on `ExternalTaskId`).
- Parallel/batched task submission to Graph.
- Caching of `userId → listId` across requests.
- Mapping multiple assignees per task; the model assumes a single `Assignee` string.
- Mapping a transcript to Graph categories, attendees, or calendar events.
- Notifying the assignee that a task has been created (Graph TODO does that natively).
- Localization of `TodoListName`; configured value is used verbatim per environment.

## Open Questions
None.

## Status: COMPLETE