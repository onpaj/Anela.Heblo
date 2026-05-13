0 errors. The warnings are pre-existing (162 of them, none in MeetingTasks code).

---

# Implementation: Meeting Task Write Handlers — Edit, Approve/Reject, Add Task

## What was implemented

Three MediatR write-side handlers for the Meeting Task Validation Checkpoint feature, plus unit tests covering all three happy paths and edge cases (not-found, invalid status).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskRequest.cs` — request DTO with `[Required]` on Title/Assignee
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskResponse.cs` — BaseResponse-derived marker class with error-code constructor
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskHandler.cs` — handler: loads transcript, locates task, mutates 4 fields, saves
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusRequest.cs` — request DTO with `[Required]` on Status
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusResponse.cs` — BaseResponse-derived marker class
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusHandler.cs` — handler: validates enum parse, transitions status
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskRequest.cs` — request DTO
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskResponse.cs` — carries `ProposedTaskDto Task`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskHandler.cs` — handler: constructs `ProposedTask` with `IsManuallyAdded=true`, appends, returns DTO
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs` — 9 tests covering happy paths + error cases for all three handlers

## Tests

`backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs` — 9 tests passing:
- `UpdateTask_ModifiesFields` — confirms all 4 fields are mutated and `SaveChangesAsync` called once
- `UpdateTaskStatus_ApprovesTask` — confirms `Approved` status is set
- `AddTask_CreatesManualTask` — confirms task appended with `IsManuallyAdded=true`, DTO populated correctly
- Additional edge-case tests for not-found transcript, not-found task, invalid status string

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdateProposedTaskHandlerTests"
# Expected: 9 passed, 0 failed

dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MeetingTasks"
# Expected: 24 passed, 0 failed
```

## Notes

- Used `ErrorCodes.ResourceNotFound` (not `ErrorCodes.NotFound` from spec) — the actual enum member, matching sibling `GetTranscriptDetailHandler`
- Each handler injects `ILogger<THandler>` and logs on entry/warning on failure — matching sibling convention
- Each response type lives in its own `*Response.cs` file per Vertical Slice convention
- Error responses use constructor-overload form (`new Response(ErrorCodes.ResourceNotFound)`) matching `GetTranscriptDetailResponse`
- Test helper uses real `MeetingTranscript` fields (`PlaudRecordingId`, `RawTranscript`) — spec's `SourceEmail` field does not exist on the entity
- `AddProposedTaskHandler` explicitly sets `ExternalTaskId = null` on the DTO for symmetry with read handler

## PR Summary

Adds three MediatR write-side handlers for the meeting task validation checkpoint: `UpdateProposedTask` (edits Title/Description/Assignee/DueDate), `UpdateProposedTaskStatus` (transitions Pending→Approved/Rejected via case-insensitive enum parse), and `AddProposedTask` (creates a manually-authored task with `IsManuallyAdded=true`). All handlers follow the established Vertical Slice convention: one UseCase folder with three files (request/response/handler), constructor-injected `IMeetingTranscriptRepository` + `ILogger<T>`, `ErrorCodes.ResourceNotFound` for not-found cases, and `BaseResponse`-derived responses with constructor-overload error signaling.

### Changes
- `UseCases/UpdateProposedTask/` — request, response, handler (edit task fields)
- `UseCases/UpdateProposedTaskStatus/` — request, response, handler (status lifecycle)
- `UseCases/AddProposedTask/` — request, response, handler (manual task creation)
- `Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs` — 9 unit tests with FluentAssertions + NullLogger + Mock.Verify

## Status
DONE