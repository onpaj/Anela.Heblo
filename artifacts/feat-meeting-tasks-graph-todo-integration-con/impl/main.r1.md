All 45 MeetingTasks tests pass. The implementation is complete and verified.

---

# Implementation: Graph TODO Integration & Controller Wiring

## What was implemented

The full Meeting Task Validation Checkpoint backend has been implemented and committed on branch `feat-meeting-tasks-graph-todo-integration-con`. All 6 commits covering the feature are present and the branch is merged with `origin/feat/meeting-task-validation-epic`.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Common/Graph/GraphApiHelpers.cs` — relocated from KnowledgeBase, made public
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs` — `TodoListName` config class
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoContracts.cs` — Graph API DTOs
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IGraphTodoService.cs` — interface + `TodoTaskResult` record
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs` — HTTP-backed Graph TODO client with OData escaping and list autocreate
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/` — `Request`, `Response`, `Handler`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` — extended with options, `AddHttpClient("MicrosoftGraph")`, `IGraphTodoService`
- `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs` — 6-endpoint authenticated REST surface
- `backend/src/Anela.Heblo.API/appsettings.json` — `MeetingTasks:TodoListName` default config

## Tests

- `GraphTodoServiceTests.cs` — 12 tests covering `ResolveUserIdAsync` (single/no/multi-match, HTTP failure, transport exception, OData single-quote escaping, token+URL verification) and `CreateTodoTaskAsync` (existing list, missing list autocreate, case-insensitive match, HTTP error, transport exception)
- `SubmitToTodoHandlerTests.cs` — 9 tests covering not-found, full success → Approved status, mixed approved+rejected → PartiallyApproved, pending blocks completion, already-submitted idempotency, unresolved assignee failure, graph failure continues batch, per-task save sequencing, re-run safety

## How to verify

```bash
# Build
dotnet build Anela.Heblo.sln --nologo --verbosity minimal

# New tests (21)
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphTodoServiceTests|FullyQualifiedName~SubmitToTodoHandlerTests"

# All MeetingTasks tests (45)
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.MeetingTasks"
```

## Notes

- Build: 0 errors, 314 pre-existing warnings (none from this feature)
- All 21 new tests pass; all 45 MeetingTasks tests pass
- Spec amendments from arch-review are applied: `ResourceNotFound` (not `NotFound`), module extended not recreated, no duplicate DI registrations, `IOptions<MeetingTasksOptions>` injection, per-task `SaveChangesAsync`, OData `'`→`''` double-escaping, class-level `[Authorize]` only

## PR Summary
Wires the Meeting Task Validation Checkpoint feature end-to-end: Graph TODO client that resolves assignees via filtered `/users` query and creates tasks in a configured per-user TODO list, a `SubmitToTodoHandler` that processes all approved tasks of a transcript with per-task persistence (idempotency), and a `MeetingTasksController` exposing the full 6-endpoint authenticated review workflow under `/api/meeting-tasks`. The shared `GraphApiHelpers` are relocated from `KnowledgeBase` to `Application.Common.Graph` to eliminate cross-feature internal coupling.

### Changes
- `backend/src/Anela.Heblo.Application/Common/Graph/GraphApiHelpers.cs` — relocated generic Graph helpers, made public
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs` — HTTP Graph client with OData escaping + list autocreate
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/` — MediatR contracts + handler
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` — extended with options, HTTP client, service registration
- `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs` — 6 authenticated REST endpoints
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs` — 12 tests
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/SubmitToTodoHandlerTests.cs` — 9 tests

## Status
DONE