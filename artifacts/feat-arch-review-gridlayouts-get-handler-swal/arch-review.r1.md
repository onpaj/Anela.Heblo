I have enough to write the review. The codebase has a `BaseApiController.HandleResponse<T>` helper that maps `ErrorCodes.DatabaseError` → HTTP 500 via an `[HttpStatusCode]` attribute, and the existing Save/Reset endpoints already return 500. This contradicts the spec's HTTP 503 proposal. I also found that the FE hook's catch branch actually does call `buildDefaultState` (resetting the layout), contradicting the spec's claim that it already preserves state.

```markdown
# Architecture Review: GetGridLayout DB Error Surfacing

## Skip Design: true

This is a backend behavioural fix plus a small frontend hook adjustment. No new components, screens, or visual design changes.

## Architectural Fit Assessment

The fix aligns with two pre-existing conventions in this codebase — but the spec's proposed status code (503) conflicts with both. Honoring the conventions makes the change a one-line behavioural alignment rather than a controller-wide deviation.

**Convention 1 — System-wide error→status mapping.**
`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:34-35` decorates `DatabaseError` with `[HttpStatusCode(HttpStatusCode.InternalServerError)]` (500). `BaseApiController.HandleResponse<T>` (`BaseApiController.cs:28-59`) uses this attribute via reflection. Every other DB-error path in the system maps to 500; modules that genuinely need "transient/retry" semantics use distinct error codes such as `DataAccessUnavailable` (1305 → 503), `ExternalServiceError` (9001 → 503), or `FileDownloadFailed` (1803 → 503).

**Convention 2 — Same-module Save/Reset behaviour.**
`GridLayoutsController.cs:36-37, 46-47` returns `StatusCode(500, response)` on `DatabaseError`. Returning 503 for Get only would make the same module behave differently for the same exception type.

**Recommendation: use HTTP 500 for Get, not 503.** The FE behavioural goal (preserve current layout on non-2xx) is fully achieved by 500 — the FE branch on "non-2xx" is what matters, not the exact code. Deviating to 503 buys nothing here and introduces inconsistency. If "retryable DB outage" semantics are ever genuinely needed, introduce a new error code (e.g. `DatabaseUnavailable`) with `[HttpStatusCode(ServiceUnavailable)]` and apply it across Get/Save/Reset uniformly — but that is out of scope.

**FE inconsistency in the spec.**
FR-4 claims the existing `useGridLayout.ts:78-80` catch branch "preserves the currently-visible layout". It does **not**. Line 80 unconditionally calls `setColumnState(buildDefaultState(columnsRef.current))`. The actual FE change is therefore larger than the spec implies: the catch branch must be modified to preserve in-memory state when state exists, and to optionally surface a toast.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────┐
│ frontend/src/features/grid-layout/useGridLayout.ts               │
│   GET load effect:                                               │
│     try { layout = apiClient.gridLayouts_Get(gridKey) }          │
│       200 + dto    → mergeStates                                 │
│       200 + null   → buildDefaultState                           │
│     catch (non-2xx, network)                                     │
│       if columnState.length === 0 → buildDefaultState (1st load) │
│       else                        → keep current state (no-op)   │
│       always: log + optional non-blocking toast                  │
└──────────────────────────────────────────────────────────────────┘
                              │ HTTP
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│ backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs │
│   GET /api/GridLayouts/{gridKey}                                 │
│     response = mediator.Send(GetGridLayoutRequest)               │
│     if (!response.Success) return StatusCode(500, response)      │
│     return Ok(response.Layout)                                   │
└──────────────────────────────────────────────────────────────────┘
                              │ MediatR
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│ Application/Features/GridLayouts/UseCases/GetGridLayout/         │
│   GetGridLayoutResponse                                          │
│     + ctor()              (existing implicit, success)           │
│     + ctor(ErrorCodes)    (NEW — error response)                 │
│   GetGridLayoutHandler.Handle                                    │
│     catch GridLayoutPersistenceException                         │
│       _logger.LogError(ex, …)                  (unchanged)       │
│       return new GetGridLayoutResponse(DatabaseError)  (CHANGED) │
└──────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: HTTP status code for `DatabaseError` on Get

**Options considered:**
- **(A) 500** — match `[HttpStatusCode]` attribute on `ErrorCodes.DatabaseError` and existing Save/Reset behaviour.
- **(B) 503** — spec proposal; signals "transient / retry".
- **(C) Introduce a new `DatabaseUnavailable` error code → 503, applied uniformly to all three handlers.**

**Chosen approach:** **(A) — return 500.**

**Rationale:** Internal consistency wins. The system-wide convention is encoded in the `[HttpStatusCode]` attribute and applied uniformly elsewhere. The FE preservation behaviour is triggered by any non-2xx, so 500 vs 503 has no functional FE impact. Option (C) is the correct path if 503 semantics are genuinely valuable, but that is a cross-cutting refactor outside this fix's scope.

#### Decision 2: Use `BaseApiController.HandleResponse<T>` or raw `StatusCode(...)`?

**Options considered:**
- **(A) Raw `StatusCode(500, response)`** — match the literal pattern Save/Reset already use in this controller.
- **(B) `return HandleResponse(response)`** — use the framework helper that derives status from the `ErrorCodes` attribute, both for the new Get path and (for consistency) Save/Reset.

**Chosen approach:** **(A) for this change; flag (B) as a follow-up.**

**Rationale:** Surgical changes (per `CLAUDE.md`). The Get fix mirrors Save/Reset exactly, keeping the PR scoped. Migrating all three to `HandleResponse` is a clean refactor but out of scope and risks side effects on the `Ok(response.Layout)` happy path shape.

#### Decision 3: FE catch-branch behaviour on first-load vs subsequent-load

**Options considered:**
- **(A) Always preserve in-memory state** — but on first mount `columnState` is `[]`, leaving the grid blank.
- **(B) Always reset to default** — current behaviour, what we're fixing.
- **(C) Conditional: preserve if `columnState.length > 0`, else fall back to `buildDefaultState`.**

**Chosen approach:** **(C).**

**Rationale:** Matches the spec's intent (no silent reset of a layout the user can see) while still producing a usable grid on first visit during a DB outage. The user will eventually retry/refresh; on success the saved layout reappears.

#### Decision 4: FE error surface (toast vs silent)

**Chosen approach:** **Non-blocking toast** via the existing `ToastContext` (`frontend/src/contexts/ToastContext.tsx`).

**Rationale:** The user otherwise has no signal that the layout shown may not be persisted-state. A subtle toast ("Could not load saved layout — using current view.") is the minimum actionable feedback. The toast is one-shot; it is not retried on re-renders.

## Implementation Guidance

### Directory / Module Structure

No new files. Edits only:

```
backend/
  src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/
    GetGridLayoutResponse.cs   ← add error-aware constructor
    GetGridLayoutHandler.cs    ← change catch return
  src/Anela.Heblo.API/Controllers/
    GridLayoutsController.cs   ← check response.Success in Get action
  test/Anela.Heblo.Tests/Features/GridLayouts/
    GetGridLayoutHandlerTests.cs   ← update existing DB-error test (see below)

frontend/
  src/features/grid-layout/
    useGridLayout.ts                                    ← conditional catch branch + toast
    __tests__/useGridLayout.test.ts                     ← add non-2xx preservation test
```

### Interfaces and Contracts

**`GetGridLayoutResponse` (mirror Save/Reset):**

```csharp
public class GetGridLayoutResponse : BaseResponse
{
    public GetGridLayoutResponse() : base() { }
    public GetGridLayoutResponse(ErrorCodes errorCode) : base(errorCode) { }
    public GridLayoutDto? Layout { get; set; }
}
```

DTO remains a class (project rule). The added constructor does not change the OpenAPI-generated TypeScript surface materially — TS clients see the same JSON shape.

**`GridLayoutsController.Get`:**

```csharp
[HttpGet("{gridKey}")]
public async Task<ActionResult<GridLayoutDto?>> Get(string gridKey)
{
    var response = await _mediator.Send(new GetGridLayoutRequest { GridKey = gridKey });
    if (!response.Success)
        return StatusCode(500, response);
    return Ok(response.Layout);
}
```

**`GetGridLayoutHandler` (only the catch branch changes):**

```csharp
catch (GridLayoutPersistenceException ex)
{
    _logger.LogError(ex,
        "Database error reading GridLayout for user={UserId} gridKey={GridKey}",
        userId, request.GridKey);
    return new GetGridLayoutResponse(ErrorCodes.DatabaseError);
}
```

**`useGridLayout.ts` catch branch:**

```ts
} catch (error) {
  console.warn('Failed to load grid layout:', error);
  if (cancelled) return;
  setColumnState((prev) => prev.length > 0 ? prev : buildDefaultState(columnsRef.current));
  // optional: showToast('Could not load saved layout — using current view.', 'warning')
}
```

Note: existing test at `GetGridLayoutHandlerTests.cs:58-80` (`Handle_WhenDatabaseThrows_ReturnsNullLayoutAndLogsError`) asserts `Assert.Null(response.Layout)` and is now incorrect — it must assert `response.Success == false` and `response.ErrorCode == ErrorCodes.DatabaseError`. The malformed-JSON path (line 82+) remains success-with-null and is unchanged.

### Data Flow

**Success path (unchanged):**
1. FE calls `gridLayouts_Get(gridKey)`.
2. Handler returns `GetGridLayoutResponse { Success = true, Layout = dto-or-null }`.
3. Controller returns `200 OK` with `dto` or `null`.
4. FE merges or builds defaults.

**DB error path (new):**
1. FE calls `gridLayouts_Get(gridKey)`.
2. Repository throws `GridLayoutPersistenceException`.
3. Handler logs error and returns `GetGridLayoutResponse(ErrorCodes.DatabaseError)` (`Success = false`).
4. Controller returns `500` with the full `GetGridLayoutResponse` body.
5. FE generated client throws.
6. FE catch branch: keep current `columnState` if non-empty; otherwise `buildDefaultState`. Optional toast.

**"No saved layout" path (unchanged):**
1. Repository returns `null`.
2. Handler returns `Success = true`, `Layout = null`.
3. Controller returns `200 OK` with `null` body.
4. FE `buildDefaultState`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec FR-4 incorrectly claims FE already preserves state — implementer may skip the actual FE change | High | This review flags the bug at `useGridLayout.ts:80` and prescribes the conditional. Add the non-2xx unit test in `useGridLayout.test.ts` as an explicit gate. |
| Choosing 503 over 500 fragments the module's error-status convention | Medium | Use 500 (matches `[HttpStatusCode]` attribute and Save/Reset). |
| Existing handler test (`Handle_WhenDatabaseThrows_ReturnsNullLayoutAndLogsError`) will silently keep passing the wrong assertion if not updated | Medium | Update its asserts to verify `Success == false` and `ErrorCode == DatabaseError`. |
| Returning `GetGridLayoutResponse` as the 500 body changes the response shape for that path; OpenAPI-generated TS client treats non-2xx as throw, so it is not consumed as a typed body | Low | Confirmed by reading `useGridLayout.ts` — the client throws on non-2xx, never deserializes the error envelope; no client-side schema breakage. |
| Toast on every failed load may be noisy if a user opens many grids during an outage | Low | Show toast only on first occurrence per `gridKey` per mount (already implicit — toast lives in the load `useEffect` which runs once per `gridKey`). |

## Specification Amendments

1. **Change FR-2: use HTTP 500, not 503**, to align with `ErrorCodes.DatabaseError`'s `[HttpStatusCode(InternalServerError)]` attribute and with Save/Reset in the same controller. Drop the 503 assumption and the alignment Open Question.
2. **Correct FR-4 premise.** The current `useGridLayout.ts:78-80` catch branch **does** call `setColumnState(buildDefaultState(...))`, i.e. it resets the layout — the opposite of what FR-4 states. The FE change is therefore mandatory, not "verify the existing behaviour". Updated AC:
   - When `columnState` already has entries, the catch branch leaves it untouched.
   - When `columnState` is empty (first mount), the catch branch falls back to `buildDefaultState` so the grid is usable.
   - Test: mock a 500 response, assert `columnState` is unchanged when pre-populated; assert defaults when initial.
3. **Add to FR-1 ACs:** update the existing test `GetGridLayoutHandlerTests.Handle_WhenDatabaseThrows_ReturnsNullLayoutAndLogsError` rather than adding a new one — the old assertion (`Layout == null` with implicit `Success == true`) becomes wrong.
4. **Clarify FR-5:** the controller does not need to re-log; the handler's `LogError` is sufficient. Avoid double-logging when the global exception middleware also logs.

## Prerequisites

None. The fix uses only existing types (`BaseResponse`, `ErrorCodes.DatabaseError`, `GridLayoutPersistenceException`), existing FE infrastructure (`ToastContext`), and existing test scaffolding. No migrations, config, or infrastructure changes.
```