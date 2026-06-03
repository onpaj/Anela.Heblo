# Specification: Marketing Module — Consistent DB Save Error Handling for Update and Delete Handlers

## Summary
The `UpdateMarketingActionHandler` and `DeleteMarketingActionHandler` lack the compensation/error-handling pattern that `CreateMarketingActionHandler` already implements around the DB save call. When Outlook is updated successfully but the subsequent DB save fails, the exception propagates unhandled — surfacing as HTTP 500 instead of the module's structured `ErrorCodes.DatabaseError` response — and Outlook silently diverges from the database. This spec defines a consistent guarded-save + structured-error pattern across all three handlers.

## Background
The Marketing module uses MediatR handlers that synchronize a marketing action between the local database and an Outlook calendar event. The Outlook write is performed first; the DB write is performed second. If the DB write fails after a successful Outlook write, the two systems diverge.

`CreateMarketingActionHandler` (lines 87–112) already documents the intended pattern: a try/catch around the DB save that (a) logs the inconsistency, (b) attempts a compensating Outlook delete, and (c) returns `ErrorCodes.DatabaseError` so the caller receives a structured failure rather than an unhandled exception.

`UpdateMarketingActionHandler` (lines 112–113) and `DeleteMarketingActionHandler` (lines 75–76) skip this pattern entirely. The result:

- **Inconsistent error surface**: every other handler in the module returns a typed response with an error code; these two leak raw 500s.
- **Silent divergence**: on transient DB errors (deadlock, connection drop, timeout), Outlook reflects the new state while the database keeps the old. Users see the team calendar move but the app shows no change.
- **Maintainer confusion**: the Create handler establishes the convention; the other two violate it without explanation.

A full transactional rollback of an Outlook update is not feasible (it would require recording the original Outlook field values and re-issuing a PATCH); the practical minimum is to **detect, log, and report** the inconsistency through the existing response-object pattern.

## Functional Requirements

### FR-1: Guard the DB save in `UpdateMarketingActionHandler`
Wrap the `UpdateAsync` + `SaveChangesAsync` calls in `UpdateMarketingActionHandler` (around lines 112–113) in a try/catch that mirrors the pattern in `CreateMarketingActionHandler`. On exception:
1. Log an error at `LogError` level that includes the exception, the `MarketingAction` ID, the `OutlookEventId`, and an explicit message stating that Outlook and the database may now be out of sync.
2. Return `new UpdateMarketingActionResponse(ErrorCodes.DatabaseError)` so the caller receives a structured failure response rather than a 500.
3. Do **not** attempt to revert the Outlook event (out of scope — see Out of Scope).

**Acceptance criteria:**
- A unit test that forces `SaveChangesAsync` to throw (e.g. via mocked repository) on update returns an `UpdateMarketingActionResponse` with `ErrorCode == ErrorCodes.DatabaseError` and does **not** throw.
- The same test asserts that an error-level log entry is emitted containing the action ID and the Outlook event ID.
- An existing happy-path unit test for `UpdateMarketingActionHandler` continues to pass without modification.
- The exception is no longer propagated to MediatR; the API integration test for a failing update returns the structured error envelope, not HTTP 500.

### FR-2: Guard the DB save in `DeleteMarketingActionHandler`
Wrap the `_repository.DeleteSoftAsync` call in `DeleteMarketingActionHandler` (around lines 75–76) — which internally performs `SaveChangesAsync` — in the same try/catch pattern. On exception:
1. Log an error including the exception, the action ID, and the (now-deleted) Outlook event ID, with an explicit message stating the Outlook event has already been removed while the DB row remains.
2. Return the delete handler's typed response with `ErrorCodes.DatabaseError` (use whichever response class the handler currently constructs for success; if none exists for the error case, follow the same shape as the other handlers in the module).
3. Do **not** attempt to recreate the Outlook event (out of scope).

**Acceptance criteria:**
- A unit test that forces `DeleteSoftAsync` to throw returns the typed response with `ErrorCode == ErrorCodes.DatabaseError` and does not throw.
- The test asserts an error-level log entry referencing the action ID and the deleted Outlook event ID.
- The existing happy-path delete unit test continues to pass.
- API integration test for a failing delete returns the structured error envelope, not HTTP 500.

### FR-3: Consistent log message format across handlers
All three handlers (Create, Update, Delete) must emit the post-DB-failure log message with:
- A clear statement of which operation failed (create/update/delete).
- The `MarketingAction.Id` (where available — on create it may be the in-memory value).
- The `OutlookEventId`.
- An explicit "Outlook and DB may be out of sync" / "Outlook event already deleted" phrase so the message is greppable for ops.

**Acceptance criteria:**
- A grep for "out of sync" / "may now be out of sync" / "already deleted" returns at least one match per handler.
- Log messages use structured logging placeholders (`{ActionId}`, `{EventId}`), not string interpolation.

### FR-4: Preserve existing Create handler behavior
No functional change to `CreateMarketingActionHandler`. Its compensating-Outlook-delete behavior is the reference implementation and must continue to work. If a minor refactor (e.g. extracting a shared helper) is proposed during implementation, it must keep the Create handler's compensation path intact.

**Acceptance criteria:**
- Existing `CreateMarketingActionHandler` unit tests pass without modification.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact expected. The change adds only a try/catch wrapper; the catch path executes only on failure (already a rare, slow path involving I/O).

### NFR-2: Security
No new attack surface. Log messages must not include sensitive marketing-action payload fields (description, customer data); they must only include identifiers (`ActionId`, `OutlookEventId`).

### NFR-3: Observability
The error logs must be sufficient for an on-call engineer to identify drift between Outlook and the DB without needing additional instrumentation. Specifically: given the log entry, an operator must be able to (a) locate the `MarketingAction` row by ID and (b) locate the Outlook event by `OutlookEventId`.

### NFR-4: Consistency / Maintainability
After this change, all three handlers in the Marketing module must follow the same guarded-save shape, so a future developer adding a fourth handler can copy any of them as a template.

## Data Model
No schema changes. The change is purely behavioral in three handler classes.

Entities involved (unchanged):
- `MarketingAction` — local DB entity with `Id`, `OutlookEventId`, and other fields.
- Outlook calendar event — external, referenced by `OutlookEventId`.

## API / Interface Design
No public API contract changes. The affected MediatR responses are:
- `UpdateMarketingActionResponse(ErrorCodes errorCode)` — already supports an error-code constructor (verified by Create handler precedent); the Update path will now use it on DB failure instead of leaking an exception.
- Delete handler response — same pattern; uses whichever response shape the handler already defines.

Externally observable change: API endpoints for update and delete will return a structured `{ success: false, errorCode: "DatabaseError" }` envelope on DB save failure instead of HTTP 500. This is a **behavior fix**, not a breaking change — callers that relied on 500 to detect failure should already be checking the response envelope per module convention.

## Dependencies
- Existing `ErrorCodes` enum/constant in the Marketing module — must contain `DatabaseError` (already used by Create handler).
- Existing repository interface (`UpdateAsync`, `SaveChangesAsync`, `DeleteSoftAsync`).
- Existing `ILogger<T>` injected into each handler.
- MediatR pipeline — no changes.

No new packages, no new services, no infrastructure changes.

## Out of Scope
- **Reverting / compensating Outlook updates** on DB failure in the Update handler. A true compensation would require capturing the pre-update Outlook field values and re-issuing a PATCH; this adds substantial complexity and is not required by this fix. The minimum viable behavior is "log + report".
- **Recreating Outlook events** on DB failure in the Delete handler. Same reasoning.
- **Introducing a distributed transaction / outbox pattern** between Outlook and the database. A proper two-phase or outbox-based solution is the long-term answer to this class of bug but is a separate, much larger initiative.
- **Reordering Outlook and DB writes** (e.g. DB first, Outlook second). This would shift the inconsistency window but not eliminate it, and it changes the user-visible behavior of all three handlers — out of scope here.
- **Adding retry logic** for transient DB errors. Could be added later via EF Core's retry-on-failure execution strategy; not in this fix.
- **Extracting a shared base class or helper** for the guarded-save pattern. Allowed if trivially clean, but not required; three near-identical try/catch blocks are acceptable given the small surface area.
- Changes to `CreateMarketingActionHandler` beyond what is needed to keep tests passing.

## Open Questions
None.

## Status: COMPLETE