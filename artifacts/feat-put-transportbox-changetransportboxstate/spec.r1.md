# Specification: Diagnose and remediate HTTP 409 spike on `PUT /api/transport-boxes/{id}/state`

## Summary

Production telemetry shows a spike of HTTP 409 Conflict responses on the transport-box state-change endpoint (mediator: `ChangeTransportBoxStateHandler`, route: `PUT /api/transport-boxes/{id}/state`). The only error path in the Transport module that maps to 409 is `TransportBoxDuplicateActiveBoxFound`, raised when a `New → Opened` transition supplies a `BoxCode` that is already held by another box in an "active" state. This spec defines the work needed to (a) confirm the cause via richer telemetry, (b) close the time-of-check-to-time-of-use (TOCTOU) race that allows duplicates to slip past the application-level check, and (c) align the `IsBoxCodeActiveAsync` definition of "active" with the actual state machine.

## Background

`TransportBoxController.ChangeTransportBoxState` dispatches `ChangeTransportBoxStateRequest` to `ChangeTransportBoxStateHandler`. The handler routes to `HandleNewToOpened` when the requested transition is `New → Opened`. Inside that callback:

1. The supplied `BoxCode` is normalized to upper case.
2. `ITransportBoxRepository.IsBoxCodeActiveAsync(normalizedCode)` is called against `ApplicationDbContext`.
3. If `true`, the handler returns `ChangeTransportBoxStateResponse { Success = false, ErrorCode = TransportBoxDuplicateActiveBoxFound }`.
4. `BaseApiController.HandleResponse` looks up the `[HttpStatusCode(HttpStatusCode.Conflict)]` attribute on the enum value and emits HTTP 409.

`IsBoxCodeActiveAsync` (in `TransportBoxRepository`) currently treats these states as "active": `New`, `Opened`, `InTransit`, `Received`, `Reserve`. **`Quarantine` is missing**, even though `Quarantine` is reachable from `Opened` (via `HandleOpenToQuarantine`) and a box in `Quarantine` still owns a `Code` — this is an existing inconsistency, not a regression.

The check is not protected by a database-level constraint: there is no filtered unique index on `Code` for active states. Two concurrent `New → Opened` requests with the same code can both observe `IsBoxCodeActiveAsync = false`, both call `UpdateAsync/SaveChangesAsync`, and both succeed — producing the very duplicate the check is meant to prevent. Once duplicates exist, every subsequent `New → Opened` against the same code will deterministically return 409, which matches the "spike" pattern in App Insights (one historic duplicate creates an ongoing tail of 409s rather than a single transient blip).

The relevant code paths:

- `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs` → `ChangeTransportBoxState` (route `PUT /api/transport-boxes/{id}/state`).
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` → `Handle` and `HandleNewToOpened`.
- `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs` → `IsBoxCodeActiveAsync`.
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` → `TransportBoxDuplicateActiveBoxFound = 1405` (mapped to HTTP 409).

The brief's "auto-generated" route name (`PUT TransportBox/ChangeTransportBoxState`) maps onto the actual route `PUT /api/transport-boxes/{id}/state`. The brief's three candidate causes (duplicate active code, concurrent update conflict, invalid terminal-state transition) are evaluated in `## Open Questions` and resolved below.

## Functional Requirements

### FR-1: Diagnose the actual 409 source in production

Add structured logging at every place that produces a 409 from this endpoint, sufficient to confirm whether the spike is duplicate-code, concurrency, invalid-transition, or something else.

**Acceptance criteria:**

- When `HandleNewToOpened` returns `TransportBoxDuplicateActiveBoxFound`, the handler emits `LogWarning` with structured fields: `BoxId`, `RequestedBoxCode` (normalized), `CurrentState`, `RequestedNewState`, and `ConflictReason = "DuplicateActiveBoxCode"`.
- When `HandleNewToOpened` returns the duplicate error, the log entry also includes the `BoxId` and `State` of the conflicting box (looked up via `GetByCodeAsync` or an equivalent single-row query — only when a conflict is detected, not on the happy path).
- The handler's top-level `catch (Exception)` block adds `RequestedNewState`, `CurrentBoxState` (if `box != null`), and the request's `BoxCode`/`Location` to the log.
- All log messages use Serilog-style structured properties (no string interpolation into the message template).
- An App Insights query in the PR description demonstrates how to filter for `ConflictReason = "DuplicateActiveBoxCode"` over the last 24 h.

### FR-2: Close the duplicate-code race with a database-level constraint

Replace the application-only "check then write" with a database-enforced uniqueness rule, so that two concurrent transactions cannot both pass the check.

**Acceptance criteria:**

- A filtered unique index is added to `TransportBox.Code` covering exactly the states currently treated as active by the application: `New`, `Opened`, `InTransit`, `Received`, `Reserve`, `Quarantine` (see FR-3 — `Quarantine` is added in this work). Rows where `Code IS NULL` or `State` is outside this set are excluded from the index.
- A new EF Core migration creates the index. The migration is idempotent and includes a pre-flight `SELECT` (or a documented manual SQL step) that lists existing duplicate `(Code, State ∈ active)` rows so the operator can resolve them before applying the migration; the migration itself fails fast if duplicates remain rather than silently succeeding.
- `ChangeTransportBoxStateHandler` catches `DbUpdateException` whose inner exception indicates a unique-constraint violation on this index, and returns `ChangeTransportBoxStateResponse { Success = false, ErrorCode = TransportBoxDuplicateActiveBoxFound, Params = { code } }` — preserving existing API behavior for clients while making the guarantee race-free.
- The pre-write `IsBoxCodeActiveAsync` check is retained as a fast-path so the common case still returns a clean error without a database round-trip into the failed transaction; the constraint catches only the racing tail.
- No retry/backoff loop is introduced — duplicates are user-facing errors, not transient infrastructure failures.

### FR-3: Align `IsBoxCodeActiveAsync` with the actual state machine

`Quarantine` is reachable from `Opened` and a box in `Quarantine` still owns a `Code`, but `IsBoxCodeActiveAsync` does not include it in its active-state set. A code held by a quarantined box can therefore be reassigned to a new box, producing two boxes that both legitimately claim the same code.

**Acceptance criteria:**

- `IsBoxCodeActiveAsync` includes `Quarantine` in its `activeStates` array.
- The filtered unique index from FR-2 likewise includes `Quarantine`.
- A unit test covers the case "open box A with code B001 → transition to Quarantine → attempt to open box B with code B001 → expect `TransportBoxDuplicateActiveBoxFound`" and replaces or extends the implicit assumption baked into existing tests.
- Existing passing tests in `TransportBoxUniquenessTests` continue to pass without modification, except where their assertions were silently exercising the missing-Quarantine bug — those are updated and the change is called out in the PR.

### FR-4: Verify the brief's other two candidate causes are not contributing

The brief lists "concurrent update conflict (optimistic concurrency)" and "invalid state transition (e.g. from a terminal state)" as alternative 409 causes. Confirm or rule each out with explicit evidence and document the result.

**Acceptance criteria:**

- The PR description documents that:
  - **Optimistic concurrency**: `TransportBox` exposes a `ConcurrencyStamp` field, but no error path in this handler maps an EF Core `DbUpdateConcurrencyException` onto an HTTP 409 today. Either show via App Insights that no such exceptions are being thrown on this endpoint, or — if they are — add an explicit catch that logs the box id and rethrows / returns a structured error (out of scope for this fix unless evidence exists).
  - **Invalid transitions**: invalid transitions raise `ValidationException` in `TransportBox.CheckState`, which the handler converts to `ErrorCodes.ValidationError` (HTTP 400, not 409). Therefore invalid-transition attempts cannot be the source of the 409 spike. Documented and closed.
- No code change is made for these two candidates beyond what is required for FR-1's structured logging.

## Non-Functional Requirements

### NFR-1: Performance

- The added structured logging fires only on the unhappy path. The happy path adds at most one extra `Include`-free `SELECT TOP 1` against `TransportBox` (FR-1: looking up the conflicting box id) and only when a duplicate is detected, so steady-state latency is unchanged.
- The filtered unique index from FR-2 is selective (excludes `NULL` codes and closed/error/stocked/inswap rows) and therefore should not measurably affect insert or update throughput on `TransportBox`.

### NFR-2: Security

- No authentication or authorization changes. The endpoint remains `[Authorize]` at the controller level.
- No PII is added to log fields. `BoxCode`, `BoxId`, and `TransportBoxState` values are operational identifiers, not personal data.
- The structured error returned to the client (`TransportBoxDuplicateActiveBoxFound` with the offending code) does not leak any data the client did not already submit.

### NFR-3: Operability

- A runbook entry (or a section in the PR description) describes:
  1. The KQL/App Insights query for filtering 409s by `ConflictReason`.
  2. The SQL query that lists currently-duplicate `(Code, State ∈ active)` rows.
  3. The remediation sequence operators should run before applying the migration if duplicates exist.

### NFR-4: Backwards compatibility

- The wire contract of `PUT /api/transport-boxes/{id}/state` is unchanged: same request shape, same `ChangeTransportBoxStateResponse` envelope, same `ErrorCode` value, same HTTP 409 status on duplicate.
- The TypeScript OpenAPI client does not need regeneration; no DTO fields are added or removed.

## Data Model

No new entities. One existing entity is amended with a database-level constraint:

- `TransportBox` — gains a filtered unique index on `Code` over `State ∈ { New, Opened, InTransit, Received, Reserve, Quarantine }`.
- All other fields remain unchanged: `Code` (string?), `State` (enum), `ConcurrencyStamp` (string), `Location`, `Description`, audit fields, `_items`, `_stateLog`.

## API / Interface Design

No API surface changes. The 409 response body remains:

```json
{
  "success": false,
  "errorCode": 1405,
  "params": { "code": "B001" }
}
```

`HandleResponse` continues to map `TransportBoxDuplicateActiveBoxFound` to HTTP 409 via the `[HttpStatusCode]` attribute on the enum.

Internal-only changes:

- `ChangeTransportBoxStateHandler.HandleNewToOpened` gains a `try` / `catch (DbUpdateException)` around the eventual `SaveChangesAsync` path (or the catch is added at the `Handle` level if cleaner) that detects the unique-index violation and returns the same structured error.
- A new private helper that classifies a `DbUpdateException` as "duplicate active box code" vs. other failures (so unrelated DB errors keep returning `TransportBoxStateChangeError` / 500-class).
- `IsBoxCodeActiveAsync` adds `TransportBoxState.Quarantine` to its `activeStates` array.

## Dependencies

- **EF Core** — for the new migration and `DbUpdateException` handling.
- **PostgreSQL** (the project's DB) — supports `CREATE UNIQUE INDEX ... WHERE` for filtered indexes.
- **Application Insights** — for confirming the diagnosis after deploy. No SDK changes.
- **`Anela.Heblo.Persistence` migrations workflow** — note: per project facts, **migrations are applied manually**, not automatically on deploy.

## Out of Scope

- Implementing retry/exponential backoff in the handler. The brief lists this as a candidate; we explicitly reject it because duplicates are user-facing conflicts, not transient infrastructure faults — retrying would just produce the same 409.
- Adding a distributed lock around state transitions. The DB-level filtered unique index is a strictly better solution: it provides the same correctness guarantee without an additional infrastructure component.
- Refactoring the `CallBackMap` dispatch in `ChangeTransportBoxStateHandler` or restructuring the state machine.
- Changes to the frontend transport-box UI flows or any related E2E tests, unless an existing test breaks.
- Deduplicating historical `TransportBox` rows where two active boxes already share a code — this is an operational task documented in the runbook (NFR-3) and performed by the operator before applying the migration.
- Wider audit of other endpoints returning 409 (`ManufactureDifficultyConflict`, `KnowledgeBaseFeedbackAlreadySubmitted`, etc.).
- Changes to box-code *generation* (the brief mentions "audit how box codes are generated" — codes are user-supplied through the UI/scanner, not generated server-side, so there is nothing to audit on generation).

## Open Questions

None.

## Status: COMPLETE
