# Architecture Review: Graph TODO Integration & Controller Wiring

## Skip Design: true

Backend-only feature — no UI components, screens, or visual design decisions are introduced. The frontend consumes these endpoints in a separate subtask.

## Architectural Fit Assessment

The feature aligns well with existing patterns: MediatR vertical-slice handlers in `Features/MeetingTasks/UseCases/<UseCase>/`, controller inheriting `BaseApiController` and dispatching to `HandleResponse(...)`, named `HttpClient("MicrosoftGraph")` driven by `Microsoft.Identity.Web.ITokenAcquisition` with `https://graph.microsoft.com/.default` scope (mirrors `GraphOneDriveService`, `PhotobankGraphService`, `OutlookCalendarSyncService`). The `IGraphTodoService` is a per-feature service that wraps Graph TODO endpoints — appropriately scoped, not shared.

However, the spec has several drift points against the actual codebase that must be corrected before implementation:

1. `ErrorCodes.NotFound` does not exist — codebase uses `ErrorCodes.ResourceNotFound` (existing `GetTranscriptDetailHandler` already does).
2. `MeetingTasksModule` **already exists** on `feat/meeting-task-validation-epic` and is registered in `ApplicationModule.cs` between `AddPhotobankModule` and `AddSmartsuppModule` — not "around line 72" after `AddMarketingInvoicesModule`. The implementation must **modify** the existing module, not create it.
3. `IMeetingTranscriptRepository` is registered in `PersistenceModule.cs` (`AddScoped<IMeetingTranscriptRepository, MeetingTranscriptRepository>()`). Re-registering in `MeetingTasksModule` is a duplicate that obscures ownership.
4. `PlaudPollingJob` implements `IRecurringJob` and is auto-discovered as **Scoped** by `AddRecurringJobs()` (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:346`). The brief's `AddTransient<PlaudPollingJob>()` would override the lifetime and break the recurring-job contract.
5. `GraphApiHelpers` is declared `internal static` in `Anela.Heblo.Application.Features.KnowledgeBase.Services`. Reaching into another feature's `internal` types violates vertical-slice boundaries even though it compiles within the same assembly.
6. The brief's `ITokenAcquisition` mock signature `(string, null, CancellationToken)` matches neither `Microsoft.Identity.Web.ITokenAcquisition.GetAccessTokenForAppAsync(string, string?, TokenAcquisitionOptions?)` nor the existing call style `GetAccessTokenForAppAsync(GraphScope)`. Tests will fail to compile.
7. Required prerequisite: **Subtask 4 (Write Handlers)** must be merged into `feat/meeting-task-validation-epic` first — the controller imports `UpdateProposedTaskRequest`, `UpdateProposedTaskStatusRequest`, `AddProposedTaskRequest`/`Response`, which currently exist only as spec artifacts.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────────┐
│                         Anela.Heblo.API                            │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │ MeetingTasksController : BaseApiController  [Authorize]      │  │
│  │   GET    /api/meeting-tasks                                  │  │
│  │   GET    /api/meeting-tasks/{id}                             │  │
│  │   PUT    /api/meeting-tasks/{tId}/tasks/{taskId}             │  │
│  │   PUT    /api/meeting-tasks/{tId}/tasks/{taskId}/status      │  │
│  │   POST   /api/meeting-tasks/{tId}/tasks                      │  │
│  │   POST   /api/meeting-tasks/{tId}/submit  ───────┐           │  │
│  └─────────────────┬────────────────────────────────┘           │  │
└────────────────────┼─────────────────────────────────────────────┘  │
                     │ MediatR.Send                                   │
                     ▼                                                │
┌─────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application.Features.MeetingTasks                       │
│                                                                     │
│  UseCases/SubmitToTodo/                                             │
│   ├ SubmitToTodoRequest (TranscriptId)                              │
│   ├ SubmitToTodoResponse : BaseResponse                             │
│   └ SubmitToTodoHandler ───┐                                        │
│                            │                                        │
│  Services/                 ▼                                        │
│   ├ IGraphTodoService ───► GraphTodoService                         │
│   │                          ├ ResolveUserIdAsync                   │
│   │                          ├ CreateTodoTaskAsync                  │
│   │                          └ GetOrCreateTodoListAsync             │
│   │                                                                 │
│  MeetingTasksOptions (TodoListName)                                 │
│  MeetingTasksModule.AddMeetingTasksModule (existing — extended)     │
└──────────┬───────────────────────────┬──────────────────────────────┘
           │ IMeetingTranscriptRepo    │ HttpClient("MicrosoftGraph") +
           │                           │ ITokenAcquisition (app-only)
           ▼                           ▼
   ┌──────────────────┐       ┌───────────────────────────┐
   │ Persistence /    │       │ Microsoft Graph v1.0      │
   │ EF Core          │       │  /users?$filter=...       │
   │  (existing)      │       │  /users/{id}/todo/lists   │
   └──────────────────┘       │  /users/{id}/todo/lists/  │
                              │      {listId}/tasks       │
                              └───────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Reuse vs. relocate `GraphApiHelpers`

**Options considered:**
- A. Reach into `Anela.Heblo.Application.Features.KnowledgeBase.Services.GraphApiHelpers` (internal-but-same-assembly) as the brief proposes.
- B. Move `GraphApiHelpers`, `GraphDriveItem`, `GraphFileFacet`, etc. (the truly generic helpers — `GraphBaseUrl`, `CreateRequest`, `DeserializeAsync`, `EncodePath`, `JsonOptions`) to `Anela.Heblo.Application.Common.Graph` and make them `public`. Leave `GraphDriveItem`/`GraphFileFacet`/`GraphDriveItemCollection` in KnowledgeBase where they're consumed.
- C. Duplicate the helpers privately inside `GraphTodoService`.

**Chosen approach:** B — extract the generic helpers to a shared namespace.

**Rationale:** The pattern is already used by 4+ modules (`KnowledgeBase`, `Photobank`, `Marketing`, now `MeetingTasks`). Cross-feature internal reach (A) breaks vertical-slice independence and creates an unstated coupling that will tempt further leaks. Duplication (C) violates DRY for code that's already battle-tested. Extraction is a small, low-risk refactor; it does not touch business logic.

#### Decision 2: `IGraphTodoService` configuration injection

**Options considered:**
- A. Brief's approach: factory in `MeetingTasksModule` that calls `configuration.GetSection(...).Get<MeetingTasksOptions>()` and passes the raw `TodoListName` string into the constructor.
- B. Constructor-inject `IOptions<MeetingTasksOptions>` directly into `GraphTodoService` and let DI build it with `services.AddScoped<IGraphTodoService, GraphTodoService>()`.

**Chosen approach:** B.

**Rationale:** A duplicates configuration binding (once via `Configure<>`, again via `Get<>` in the factory). It also denies the service the ability to react if `IOptionsMonitor`/`IOptionsSnapshot` is introduced later. B is idiomatic, simpler, and matches how every other `*Options` is consumed in this codebase.

#### Decision 3: Persistence granularity in `SubmitToTodoHandler`

**Options considered:**
- A. One `SaveChangesAsync` at the end of the loop (brief).
- B. `SaveChangesAsync` after each successful TODO task creation; final save for the status/ReviewedAt transition.

**Chosen approach:** B.

**Rationale:** Each successful Graph POST creates an externally-visible side effect (the user sees a new TODO item). If the process crashes mid-loop with A, all `ExternalTaskId` updates are lost and re-running `/submit` will recreate the same Graph tasks (NFR-3 idempotency claim is broken — idempotency only holds when the local DB reflects what's already in Graph). Saving per task closes the window to one task. Trade-off — N+1 saves for N tasks — is acceptable because submissions are user-initiated, bounded, and per-transcript (≤ 20 tasks per NFR-1).

#### Decision 4: HTTP client registration ownership

**Options considered:**
- A. Rely on `KnowledgeBaseModule`/`PhotobankModule`/`MarketingModule` to register `"MicrosoftGraph"` (current implicit assumption).
- B. Register `services.AddHttpClient("MicrosoftGraph")` in `MeetingTasksModule` too (idempotent via .NET DI).

**Chosen approach:** B.

**Rationale:** `KnowledgeBaseModule`'s registration is **conditional** (only when `sharePointConfigured && !useMockAuth && !bypassJwtValidation`). If those flags differ in any environment, the named client may not exist and `GraphTodoService` fails at runtime with a confusing factory error. `AddHttpClient` with the same name is safe to call multiple times.

## Implementation Guidance

### Directory / Module Structure

New files (relative to repo root):

```
backend/src/Anela.Heblo.Application/
  Common/Graph/                                       # NEW shared namespace
    GraphApiHelpers.cs                                # moved from KnowledgeBase, made public
  Features/MeetingTasks/
    MeetingTasksOptions.cs                            # NEW
    Services/
      IGraphTodoService.cs                            # NEW
      GraphTodoService.cs                             # NEW
      GraphTodoContracts.cs                           # NEW — GraphUser, GraphTodoList, etc.
    UseCases/SubmitToTodo/
      SubmitToTodoRequest.cs                          # NEW
      SubmitToTodoResponse.cs                         # NEW
      SubmitToTodoHandler.cs                          # NEW
  Features/MeetingTasks/MeetingTasksModule.cs         # MODIFIED (not created)

backend/src/Anela.Heblo.API/
  Controllers/MeetingTasksController.cs               # NEW

backend/src/Anela.Heblo.Application/
  Features/KnowledgeBase/Services/GraphApiHelpers.cs  # MODIFIED — delete or reduce to KB-only DTOs

backend/test/Anela.Heblo.Tests/
  Features/MeetingTasks/
    GraphTodoServiceTests.cs                          # NEW
    SubmitToTodoHandlerTests.cs                       # NEW
```

`ApplicationModule.cs` is **already wired** (`services.AddMeetingTasksModule(configuration);` exists on epic branch line ~91). No change needed there.

### Interfaces and Contracts

```csharp
// Common/Graph/GraphApiHelpers.cs — relocated and promoted to public
namespace Anela.Heblo.Application.Common.Graph;

public static class GraphApiHelpers
{
    public const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    public const string GraphScope = "https://graph.microsoft.com/.default";
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public static string EncodePath(string path) => /* unchanged */;
    public static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token) => /* unchanged */;
    public static Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct) => /* unchanged */;
}
```

```csharp
// Features/MeetingTasks/Services/IGraphTodoService.cs
public record TodoTaskResult(bool Success, string? ExternalTaskId, string? Error);

public interface IGraphTodoService
{
    Task<string?> ResolveUserIdAsync(string assigneeName, CancellationToken ct = default);
    Task<TodoTaskResult> CreateTodoTaskAsync(
        string userId, string title, string description, DateTime? dueDate, CancellationToken ct = default);
}
```

```csharp
// Features/MeetingTasks/Services/GraphTodoService.cs — constructor
public GraphTodoService(
    ITokenAcquisition tokenAcquisition,                 // Microsoft.Identity.Web.ITokenAcquisition
    IHttpClientFactory httpClientFactory,
    IOptions<MeetingTasksOptions> options,
    ILogger<GraphTodoService> logger)
{ ... _todoListName = options.Value.TodoListName; }
```

```csharp
// Features/MeetingTasks/MeetingTasksOptions.cs
public class MeetingTasksOptions
{
    public const string SectionName = "MeetingTasks";
    public string TodoListName { get; set; } = "Meeting Actions";
}
```

```csharp
// Features/MeetingTasks/MeetingTasksModule.cs — MODIFIED, additions only
public static IServiceCollection AddMeetingTasksModule(this IServiceCollection services, IConfiguration configuration)
{
    services.AddOptions<MeetingTasksOptions>()
        .Bind(configuration.GetSection(MeetingTasksOptions.SectionName))
        .ValidateOnStart();

    services.AddHttpClient("MicrosoftGraph");           // idempotent — defensive
    services.AddScoped<IGraphTodoService, GraphTodoService>();

    // existing line — keep:
    services.AddScoped<IMeetingTaskExtractor, ClaudeMeetingTaskExtractor>();
    // Do NOT register IMeetingTranscriptRepository (in PersistenceModule).
    // Do NOT register PlaudPollingJob (auto-discovered by AddRecurringJobs).
    return services;
}
```

Controller: per-endpoint `[Authorize]` is redundant when class-level `[Authorize]` is present; keep class-level only. Use `sealed` (matches `ArticlesController` style). The `BaseApiController.HandleResponse<T>` returns `ActionResult<T>` which implicitly converts to `ActionResult`, so the brief's signatures compile, but prefer `ActionResult<UpdateProposedTaskResponse>` for OpenAPI generation accuracy.

### Data Flow

**`POST /api/meeting-tasks/{id}/submit` (the new flow):**

```
HTTP request
  → MeetingTasksController.SubmitToTodo
  → MediatR → SubmitToTodoHandler.Handle
     1. _repository.GetByIdAsync(transcriptId)            [single round-trip with .Include(Tasks)]
        └─ null → return ResourceNotFound
     2. approvedTasks = Tasks.Where(Status==Approved && ExternalTaskId==null)
     3. for each task:
        a. userId = _todoService.ResolveUserIdAsync(task.Assignee)
              → Graph GET /users?$filter=displayName eq '...'
              → null → errors.Add(...); continue
        b. result = _todoService.CreateTodoTaskAsync(userId, ...)
              → Graph GET /users/{id}/todo/lists (find by name)
              → Graph POST /users/{id}/todo/lists (create if missing)
              → Graph POST /users/{id}/todo/lists/{listId}/tasks
              → success → task.ExternalTaskId = id; _repository.SaveChangesAsync(); successCount++
              → failure → errors.Add(...)
     4. recompute Status:
        allDone = Tasks.All(t => t.Status==Rejected || t.ExternalTaskId != null)
        hasRejected = Tasks.Any(t => t.Status==Rejected)
        Status = allDone ? (hasRejected ? PartiallyApproved : Approved) : PendingReview
        ReviewedAt = UtcNow
     5. _repository.SaveChangesAsync()
     6. Logger.LogInformation("Submitted {Success} tasks to TODO for transcript {Id}, {Failed} failed")
     7. return SubmitToTodoResponse { SuccessCount, FailedCount, Errors }
  → HandleResponse(...) → 200 OK
```

**Status recomputation degenerate case:** an empty `Tasks` collection or all-Rejected collection yields `allDone == true`. With no Rejected, → `Approved` (vacuously). With at least one Rejected, → `PartiallyApproved`. This matches the spec but document it in code with a comment so future readers don't read it as a bug.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `ErrorCodes.NotFound` referenced by brief does not compile | HIGH | Use `ErrorCodes.ResourceNotFound` (matches sibling `GetTranscriptDetailHandler`). |
| Brief duplicates existing registrations (repo, job) with wrong lifetime | HIGH | Module additions limited to options, HTTP client, and `IGraphTodoService` only. |
| `Microsoft.Identity.Web.ITokenAcquisition` vs `Microsoft.Identity.Abstractions.ITokenAcquisition` confusion | HIGH | Use `Microsoft.Identity.Web` everywhere — matches existing `GraphOneDriveService`, `PhotobankGraphService`. Update brief's test mock signature accordingly: `Setup(t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null, false, null))` per IWeb's full signature, or use `It.IsAny<string?>(), null, null, false, null` — simplest: stub `GetAccessTokenForAppAsync(GraphScope)` exactly and pass through. |
| `GraphApiHelpers` is `internal` in another feature | MEDIUM | Extract to `Anela.Heblo.Application.Common.Graph` (Decision 1). |
| Crash between Graph POST and DB save loses `ExternalTaskId` → duplicate TODO tasks on retry | MEDIUM | `SaveChangesAsync` after each successful task creation (Decision 3). |
| `"MicrosoftGraph"` HTTP client only registered conditionally elsewhere | MEDIUM | Register defensively in `MeetingTasksModule` (Decision 4). |
| `displayName` resolution returns the *wrong* user when duplicates exist (e.g., common first names in multi-tenant directory) | MEDIUM | Document as known limitation per FR-1. Long-term: prefer `userPrincipalName` or `mail`; require Claude extractor (subtask 2) to emit UPNs not display names. Log at Information when multi-match occurs. |
| `displayName` containing a single quote (`O'Brien`) breaks OData filter even after `Uri.EscapeDataString` | LOW | Escape single quotes by doubling them (`'` → `''`) **before** `Uri.EscapeDataString`. Add a unit test for `O'Brien`. |
| Subtask 4 (write handlers) not yet merged into the epic branch when implementation starts | HIGH | See Prerequisites — must merge first or this subtask must include the write handlers. |
| `GraphTodoService.CreateTodoTaskAsync` calls `EnsureSuccessStatusCode()` on list-lookup path — a 4xx (e.g., user has no TODO licence) throws and the whole batch sees that task fail with a stack-trace-like exception message in `Errors[]` | LOW | Catch is broad; surface a friendlier error and ensure no token/PII is included. NFR-2 requires this. Strip `ex.ToString()`, keep `ex.Message` only. |
| 30-second NFR-1 budget vs sequential 3 Graph calls per task × 20 tasks = 60 calls | LOW | Acceptable under normal latency (≈200ms/call → 12s). Spec marks parallelism out of scope. |
| Logger logs `userId` (PII) | LOW | Existing pattern logs `UserId` in `GraphOneDriveService`; acceptable for server-side ops logs. Do not include `userId` in `Errors[]` (already correctly omitted). |

## Specification Amendments

The implementation deviates from the literal spec in the following ways. These should be treated as authoritative over the spec where they conflict.

1. **Error code:** Replace every reference to `Shared.ErrorCodes.NotFound` (FR-3, handler `Step 4`, tests) with `ErrorCodes.ResourceNotFound`. The brief is silently wrong — the codebase has no `NotFound` member.
2. **Module already exists:** FR-4's "module is invoked from `ApplicationModule.cs` directly after `services.AddMarketingInvoicesModule();` (around line 72)" is obsolete — the call is at line ~91 on the epic branch, between `AddPhotobankModule` and `AddSmartsuppModule`, and is already present. Replace the FR-4 "create module" instruction with "extend the existing `MeetingTasksModule`."
3. **Drop registrations in module:** Remove from FR-4 acceptance criteria: `IMeetingTranscriptRepository → MeetingTranscriptRepository (Scoped)` (lives in `PersistenceModule`) and `PlaudPollingJob (Transient)` (auto-registered Scoped by `AddRecurringJobs`).
4. **`IGraphTodoService` lifetime/registration:** Change FR-4 wording from "factory that constructs `GraphTodoService` with the resolved `TodoListName`" to "`services.AddScoped<IGraphTodoService, GraphTodoService>()` with constructor-injected `IOptions<MeetingTasksOptions>`." (Decision 2)
5. **Add `AddHttpClient("MicrosoftGraph")`** to `MeetingTasksModule` (Decision 4). Not currently in FR-4.
6. **Token acquisition library:** FR-1/FR-2 say `ITokenAcquisition` without namespace — pin to `Microsoft.Identity.Web.ITokenAcquisition` (already used by `GraphOneDriveService`, `PhotobankGraphService`). Adjust the test mock setup to match the actual signature (`GetAccessTokenForAppAsync(string scope, string? tenant = null, TokenAcquisitionOptions? options = null)` — no CancellationToken).
7. **`GraphApiHelpers` location:** Move to `Anela.Heblo.Application.Common.Graph` and make public; update `KnowledgeBase`, `Photobank`, `Marketing` references to the new location. (Decision 1) — this is the highest-impact amendment; if rejected, fall back to making the existing internal types `public` in place, but tag this as tech-debt.
8. **Per-task persistence:** Amend FR-3 acceptance criteria — "On success, persist `task.ExternalTaskId`" should call `SaveChangesAsync` immediately after assignment, not only at end of loop. (Decision 3)
9. **OData filter escaping:** FR-1 says "display name is URI-escaped." Strengthen to: "single quotes in the display name are doubled (`'` → `''`) before `Uri.EscapeDataString` is applied, per OData v4 string literal rules." Add the `O'Brien` test case.
10. **Controller `[Authorize]`:** Remove per-endpoint `[Authorize]` attributes — class-level suffices and matches `ArticlesController` style.

## Prerequisites

Before this subtask can begin implementation:

1. **Subtask 4 (Write Handlers) must be merged** into `feat/meeting-task-validation-epic`. The branch `feat-meeting-tasks-write-handlers-edit-approv` currently contains only spec artifacts. Required types referenced by `MeetingTasksController`:
   - `UpdateProposedTaskRequest`, `UpdateProposedTaskResponse`
   - `UpdateProposedTaskStatusRequest`, `UpdateProposedTaskStatusResponse`
   - `AddProposedTaskRequest`, `AddProposedTaskResponse`
2. **Branch base verification:** Feature branch (`feat-meeting-tasks-graph-todo-integration-con`) must be rebased on `origin/feat/meeting-task-validation-epic` so the epic's existing `MeetingTasksModule.cs`, domain entities, and `GetTranscriptDetailHandler` (pattern reference) are present. The current branch currently has **none** of these files — it diverged from `main` before the epic landed.
3. **Microsoft Graph app registration permissions** (admin-consented in Azure AD): `Tasks.ReadWrite.All`, `User.Read.All` — applied as **application** permissions (not delegated), since the app uses client-credentials via `GetAccessTokenForAppAsync`.
4. **Configuration value** in target environments: `MeetingTasks:TodoListName` (default `"Meeting Actions"` is acceptable).
5. **No database migration required** — this subtask reads/writes existing columns on `MeetingTranscripts`/`ProposedTasks` already created by Subtask 1's `20260512191541_AddMeetingTasksTables` migration.
6. **No new NuGet packages.** All dependencies (`MediatR`, `Microsoft.Identity.Web`, `Moq.Protected`, `xunit`) are already in the relevant `.csproj`s.