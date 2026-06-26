# Specification: GetGridLayout DB Error Surfacing

## Summary
Fix a behavioural inconsistency in the GridLayouts module where `GetGridLayoutHandler` silently swallows `GridLayoutPersistenceException` and returns HTTP 200 with a null layout, indistinguishable from the "no saved layout" case. The handler must surface database errors via the same `BaseResponse(ErrorCodes.DatabaseError)` pattern already used by `SaveGridLayoutHandler` and `ResetGridLayoutHandler`, with the controller returning a non-2xx status so the frontend can preserve the user's visible layout instead of silently resetting it.

## Background
The GridLayouts module exposes three operations: Get, Save, Reset. Save and Reset correctly use the `BaseResponse(ErrorCodes.DatabaseError)` pattern; the controller checks `response.Success` and returns HTTP 500 when persistence fails. Get diverges:

- `GetGridLayoutHandler` (`backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs:61-67`) catches `GridLayoutPersistenceException`, logs it, and returns `new GetGridLayoutResponse { Layout = null }` — `Success = true`, `Layout = null`.
- `GridLayoutsController.GetGridLayout` (`backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs:27-28`) returns `Ok(response.Layout)` without checking `response.Success`.

Symptoms during a partial DB outage:
1. Client receives HTTP 200 with `null` body — identical to the legitimate "no saved layout yet" response.
2. The frontend `useGridLayout` hook treats both responses as equivalent and falls back to `buildDefaultState`, silently resetting the user's column layout.
3. On the next successful read the saved layout reappears, leaving the user with no actionable error message and no way to distinguish a transient outage from a real "no layout" state.
4. Monitoring sees HTTP 200, so the outage is harder to detect through standard 5xx dashboards.

The frontend already has a non-2xx error branch (`useGridLayout.ts:78-80`) that preserves the currently-visible layout. The fix is therefore concentrated on the backend; once GET returns a non-2xx on DB error, the existing FE behaviour is correct.

## Functional Requirements

### FR-1: Handler must signal DB error via BaseResponse
`GetGridLayoutHandler` must return an error response (not a success-with-null) when `GridLayoutPersistenceException` is caught.

**Acceptance criteria:**
- `GetGridLayoutResponse` exposes a constructor that accepts an `ErrorCodes` value and produces a response with `Success = false` and the supplied error code, consistent with `SaveGridLayoutResponse` and `ResetGridLayoutResponse`.
- The `catch (GridLayoutPersistenceException ex)` branch in `GetGridLayoutHandler` returns `new GetGridLayoutResponse(ErrorCodes.DatabaseError)` instead of `new GetGridLayoutResponse { Layout = null }`.
- The existing error log line (severity, message, exception attachment) is preserved.
- A unit test verifies that when the repository throws `GridLayoutPersistenceException`, the handler returns `Success = false` with `ErrorCode = ErrorCodes.DatabaseError`.

### FR-2: Controller must propagate handler errors as non-2xx
`GridLayoutsController.GetGridLayout` must inspect `response.Success` and return a non-2xx status when the handler reports failure, matching the existing pattern in `SaveGridLayout` and `ResetGridLayout`.

**Acceptance criteria:**
- When `response.Success == false`, the controller returns a non-2xx response carrying the full `GetGridLayoutResponse` body (so the client sees the error code).
- When `response.Success == true`, behaviour is unchanged: the controller returns `Ok(response.Layout)`.
- The status code used is **HTTP 503 Service Unavailable** for `ErrorCodes.DatabaseError` (transient infrastructure problem). This deviates slightly from the existing Save/Reset behaviour, which returns 500 — see Open Questions for the alignment decision. *Assumption: 503 is preferred for DB outages because it conveys "try again" semantics to the client; if alignment with Save/Reset (500) is required, both Get and the existing handlers should converge on the same code.*
- A controller-level integration test asserts the non-2xx status when the handler returns a DB error.

### FR-3: "No saved layout" remains HTTP 200 with null body
The legitimate empty-layout case must continue to return HTTP 200 with `null` as the body so existing frontend logic that builds a default state still works on a first visit.

**Acceptance criteria:**
- When the repository returns `null` (no saved layout for the user/grid), the handler returns `Success = true` with `Layout = null`.
- The controller returns `Ok(null)` for this case.
- An integration test covers this path explicitly so it is not regressed by FR-1/FR-2.

### FR-4: Frontend must preserve current layout on non-2xx
The `useGridLayout` hook must treat a non-2xx GET response as a transient error and **not** overwrite the user's currently-visible layout.

**Acceptance criteria:**
- The existing catch branch at `useGridLayout.ts:78-80` is verified to handle non-2xx responses and to leave the in-memory layout untouched (no call to `buildDefaultState`, no state replacement).
- A frontend unit test simulates a 503 response from `GET /api/grid-layouts/...` and asserts the hook does not reset to defaults and surfaces a non-fatal error (toast/log) suitable for the user. *Assumption: a non-blocking toast is acceptable; if the project's UX standard is silent retry, only the "do not reset" assertion is required.*
- The "no saved layout" path (HTTP 200, null body) continues to build the default state — unchanged.

### FR-5: Logging and observability remain intact
The existing structured log on DB error must remain in place so monitoring continues to see the failure on the backend side, in addition to the new HTTP 5xx signal.

**Acceptance criteria:**
- `_logger.LogError(ex, ...)` is still invoked with the original message template and exception.
- No new log levels or sinks are introduced.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The fix is in the error path only; the happy path is untouched.
- Latency on success: unchanged.
- Latency on DB error: unchanged (single log + response construction).

### NFR-2: Security
No new attack surface.
- The error response body contains only the `ErrorCodes` enum value and the `Success` flag — no raw exception details, stack traces, or DB connection strings. This matches the existing Save/Reset behaviour.
- No authentication or authorization changes.

### NFR-3: Backwards compatibility
- The "no saved layout" response shape is unchanged (HTTP 200 + null body). Existing clients that rely on this contract are unaffected.
- The success-path JSON for `GetGridLayoutResponse.Layout` is unchanged.
- The error-path response shape is **new** for GET (it now carries `BaseResponse` fields). Any external consumers other than the in-tree React app must be checked — none are expected in this monorepo.

### NFR-4: Observability
After the fix, a DB outage on GET produces both:
- A backend `LogError` entry (as today).
- An HTTP 5xx response visible to APM / Application Insights / uptime checks.

This is the primary motivation for the change.

## Data Model
No data-model changes. Existing entities (`GridLayout`, `GridLayoutPersistenceException`, `ErrorCodes`) are reused.

The only type changing is the `GetGridLayoutResponse` DTO, which gains an error-aware constructor mirroring `SaveGridLayoutResponse`:

```csharp
public class GetGridLayoutResponse : BaseResponse
{
    public GetGridLayoutResponse() : base() { }
    public GetGridLayoutResponse(ErrorCodes errorCode) : base(errorCode) { }   // new
    public GridLayoutDto? Layout { get; set; }
}
```

(Per project rule: DTOs are classes, never C# records.)

## API / Interface Design

### Endpoint: `GET /api/grid-layouts/{gridId}` (or current route)
| Scenario | Status | Body |
|---|---|---|
| Saved layout exists | 200 OK | `GridLayoutDto` JSON |
| No saved layout for user/grid | 200 OK | `null` |
| DB unavailable / `GridLayoutPersistenceException` | **503 Service Unavailable** *(new)* | `GetGridLayoutResponse` with `Success = false`, `ErrorCode = DatabaseError` |
| Unhandled exception | 500 (existing global handler) | per existing global error contract |

### Frontend hook contract (`useGridLayout`)
| Scenario | Hook behaviour |
|---|---|
| 200 + layout | Apply layout to state (unchanged) |
| 200 + null | `buildDefaultState()` (unchanged) |
| Non-2xx | Keep current in-memory layout; surface non-blocking error (toast/log). Do **not** call `buildDefaultState`. |

## Dependencies
- `MediatR` request/response pipeline (existing).
- `BaseResponse` and `ErrorCodes` enum in the application layer (existing).
- `GridLayoutPersistenceException` (existing).
- Frontend `useGridLayout.ts` hook and its toast/error infrastructure (existing).
- No new packages, no new external services.

## Out of Scope
- Refactoring the broader error-handling contract across other modules.
- Adding retry logic in the frontend or backend (clients may retry; no automatic backoff added here).
- Changing the response shape on the happy path.
- Introducing a new error code beyond `ErrorCodes.DatabaseError`.
- Migrating Save/Reset from 500 to 503 (unless the cross-cutting alignment decision in Open Questions chooses 503 for all three).
- Telemetry/dashboard changes — out of scope; the new 5xx will flow into existing monitoring automatically.
- E2E coverage of a real DB outage (not feasible in nightly Playwright; unit + integration tests are sufficient).

## Open Questions
None.

## Status: COMPLETE