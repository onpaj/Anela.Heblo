# Architecture Review: Remove Redundant Try-Catch in GetOrganizationStructureHandler

## Skip Design: true

## Architectural Fit Assessment

The proposed change strongly aligns with existing patterns in this codebase. The reference handler `GetJournalEntriesHandler` (`backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs`) — and all other handlers I sampled — contain **no try-catch wrappers around their happy path**. The OrgChart handler is the outlier; removing its catch block brings it into conformance with the convention, not away from it.

Integration points are minimal and well-bounded:
- **MediatR pipeline** — already designed to propagate exceptions; no behavior change.
- **`OrgChartController` catch block** (lines 40–53) — already the single, authoritative point that logs the failure and shapes the 500 response.
- **Logger contract** — `ILogger<GetOrganizationStructureHandler>` is still used for the informational entry log, so the DI registration and constructor signature are unchanged.

One observation from reading `OrgChartController.cs:51`: the 500 response body currently returns `ex.Message` to the client (`new { error = "...", message = ex.Message }`). This is a pre-existing concern about leaking exception detail to API consumers — it's **out of scope** for this spec, but worth flagging for a follow-up arch review of the controller's error handling.

## Proposed Architecture

### Component Overview

```
HTTP Client
    │
    ▼
OrgChartController.GetOrganizationStructure       ◄── single try-catch
    │   (logs request, sends MediatR request,
    │    catches + logs + maps to 500)
    ▼
IMediator.Send
    │
    ▼
GetOrganizationStructureHandler.Handle             ◄── happy-path only after change
    │   (logs "Handling request...", calls service)
    ▼
IOrgChartService.GetOrganizationStructureAsync
```

After the change, exceptions thrown by `IOrgChartService` propagate unmodified through the handler and MediatR pipeline back to the controller, which performs the single log + 500 mapping.

### Key Design Decisions

#### Decision 1: Remove the catch entirely vs. keep it for "defensive" logging
**Options considered:**
- (a) Delete the try-catch — handler returns to happy-path-only.
- (b) Keep the catch but downgrade to `LogDebug` to avoid duplicate ERROR entries.
- (c) Replace with a MediatR pipeline behavior that logs all handler exceptions globally.

**Chosen approach:** (a) — delete the try-catch entirely.

**Rationale:** Option (b) leaves dead-but-noisy code in place and still violates KISS. Option (c) is the long-term direction (pipeline behavior or exception middleware) but is explicitly out of scope per the spec and would touch every handler. Option (a) is the surgical fix called for by the brief and matches every other handler in the codebase (`GetJournalEntriesHandler` and siblings have no top-level try-catch).

#### Decision 2: What to do about missing handler tests
**Options considered:**
- (a) Add a minimal xUnit test class for `GetOrganizationStructureHandler` covering happy path + exception propagation.
- (b) Skip tests entirely on the grounds that the handler is a one-liner.

**Chosen approach:** (a) — add a small test class.

**Rationale:** No tests currently exist for `OrgChart` in `backend/test/Anela.Heblo.Tests` (confirmed via search). FR-4 in the spec requires "a test exists (new or existing) verifying that an exception thrown by `_orgChartService` propagates out of `Handle` unchanged." Since there is no existing test file, one must be created — see Specification Amendments below for the resulting clarification.

## Implementation Guidance

### Directory / Module Structure

No new directories. Files affected:

```
backend/
├── src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/
│   └── GetOrganizationStructureHandler.cs        ← EDIT: remove try-catch
└── test/Anela.Heblo.Tests/Features/
    └── OrgChart/                                  ← NEW directory
        └── GetOrganizationStructureHandlerTests.cs  ← NEW file
```

The new test directory mirrors `src/Anela.Heblo.Application/Features/OrgChart/` per the C# testing convention (`Mirror src/ structure under tests/`).

### Interfaces and Contracts

No interface or contract changes:
- `IRequestHandler<GetOrganizationStructureRequest, OrgChartResponse>` — unchanged.
- `GetOrganizationStructureRequest` (empty marker request) — unchanged.
- `OrgChartResponse` — unchanged.
- `IOrgChartService.GetOrganizationStructureAsync(CancellationToken)` — unchanged.
- `OrgChartController.GetOrganizationStructure` route + response shape — unchanged.

After the edit, the handler body must be exactly:

```csharp
public async Task<OrgChartResponse> Handle(GetOrganizationStructureRequest request, CancellationToken cancellationToken)
{
    _logger.LogInformation("Handling request to fetch organizational structure");
    return await _orgChartService.GetOrganizationStructureAsync(cancellationToken);
}
```

Remove the now-unused `result` local. No `using` directives need to change — `Microsoft.Extensions.Logging` is still required for `LogInformation`, and the rest are still in use.

### Data Flow

**Success path** (unchanged client-visible behavior):
1. Controller logs `"Fetching organizational structure"` (Information).
2. Controller sends `GetOrganizationStructureRequest` via MediatR.
3. Handler logs `"Handling request to fetch organizational structure"` (Information).
4. Handler awaits `_orgChartService.GetOrganizationStructureAsync` and returns its result.
5. Controller wraps the result in `Ok(...)` → HTTP 200 + `OrgChartResponse`.

**Failure path** (one fewer log entry, otherwise identical):
1. Controller logs `"Fetching organizational structure"` (Information).
2. Handler logs `"Handling request..."` (Information) and calls the service.
3. Service throws.
4. Exception propagates **unmodified** through the handler (no log) and MediatR.
5. Controller's catch logs `"Error fetching organizational structure"` (Error) **once**, returns HTTP 500.

Net effect: one ERROR log per failed request instead of two; all other log lines unchanged.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| An observability dashboard/alert is keyed on the handler's `"Error fetching organizational structure"` log line emitted from the `Application` assembly's logger category (`GetOrganizationStructureHandler`). | Low | The controller emits the identical message text under a different category (`OrgChartController`). Search log/alert configs (`appsettings*.json`, Azure Monitor / Application Insights queries, Grafana) for the logger category `…GetOrganizationStructureHandler` before merge. Note: the spec says this is a solo-developer project, so the risk surface is small. |
| A future MediatR pipeline behavior or exception middleware is added that assumes handlers self-log on failure. | Low | Document in the commit/PR that handlers in this codebase do not self-log exceptions — the controller (or, later, a global filter) owns that responsibility. |
| Removing the catch changes the stack trace shape in the single remaining error log (slightly shallower, no `Handler.Handle` frame above the `await`). | Low | Acceptable — the controller's `LogError(ex, …)` still captures the full inner stack from the service throw site. |
| New test file is placed in a path that doesn't match the existing `Anela.Heblo.Tests` project convention. | Low | Place under `backend/test/Anela.Heblo.Tests/Features/OrgChart/` to mirror `src/Anela.Heblo.Application/Features/OrgChart/`, matching `Features/Journal/`, `Features/Catalog/`, etc. The xUnit project will auto-include it. |

## Specification Amendments

1. **FR-4 clarification — no existing handler tests.** A search of `backend/test/` returns zero files referencing `OrgChart`. The spec's "Any existing handler tests asserting on the redundant catch behavior must be updated" is vacuously satisfied. The spec's positive requirement — "A test exists (new or existing) verifying that an exception thrown by `_orgChartService` propagates out of `Handle` unchanged" — therefore requires **creating** a new test file. Implementation must produce:
   - `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs` with at minimum:
     - `Handle_ReturnsServiceResponse_WhenServiceSucceeds`
     - `Handle_PropagatesException_WhenServiceThrows` (asserts `await Assert.ThrowsAsync<…>(() => handler.Handle(...))` and verifies the exception instance/type is unchanged).
   - Use xUnit + FluentAssertions + NSubstitute or Moq per the existing test project's conventions (inspect a peer test class such as one under `Features/Journal/` to match exact mock framework choice).
   - No assertion on any handler-emitted `LogError` (such an assertion would be wrong post-change).

2. **NFR-4 observability — clarification of "at least as rich".** The pre-change handler log was `LogError(ex, "Error fetching organizational structure")`. The controller log is `LogError(ex, "Error fetching organizational structure")` — verbatim identical message text plus the same exception object. Information content is **equal**, satisfying "at least as rich." No controller change required. Record this verification in the PR description rather than re-opening it as an Open Question.

3. **Note on controller `ex.Message` exposure (not in scope, but worth recording).** `OrgChartController.cs:51` returns `ex.Message` in the 500 response body. This pre-dates the current change and is explicitly out of scope per the spec, but should be tracked as a separate finding for a future arch review (potentially together with a move to a global exception middleware as outlined in "Out of Scope").

## Prerequisites

None. The change is purely an in-place edit of one file plus the addition of one test file. No migrations, no config changes, no infrastructure changes, no new dependencies. The existing `dotnet build` + `dotnet format` + `dotnet test` validation gates from `CLAUDE.md` are sufficient to verify the change before commit.