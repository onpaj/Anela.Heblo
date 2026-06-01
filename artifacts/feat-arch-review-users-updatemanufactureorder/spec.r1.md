# Specification: Refactor UpdateManufactureOrderStatusHandler to use ICurrentUserService

## Summary
Replace the direct `IHttpContextAccessor` dependency in `UpdateManufactureOrderStatusHandler` with the existing `ICurrentUserService` abstraction so that the handler resolves the current user through the same Entra ID claim fallback chain used elsewhere in the application. This eliminates a duplicate, divergent user-resolution path that currently causes audit trail entries to be stamped as `"System"` for authenticated users when the `Identity.Name` claim is absent.

## Background
The Application layer introduced `ICurrentUserService` as the single point of truth for resolving the current user from an Entra ID access token. Its implementation (`CurrentUserService.GetCurrentUser()`) walks an ordered chain of claims (`preferred_username`, `upn`, `oid`, `sub`, …) plus `Identity.Name` to robustly identify the caller, and exposes the display value via `CurrentUserExtensions.GetDisplayName()`.

`UpdateManufactureOrderStatusHandler` (in `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`) was implemented before — or in parallel with — that abstraction and bypasses it. It injects `IHttpContextAccessor` directly, exposes a private `GetCurrentUserName()` that returns `HttpContext.User.Identity.Name ?? "System"`, and writes that value to the manufacture-order audit fields (`StateChangedByUser` and any other call sites).

Concrete consequences observed today:
- Entra ID access-token flows frequently omit the `Name` identity claim. In those flows the handler stamps `"System"` on state-change records even though the request is fully authenticated, polluting the audit trail.
- The handler is the second direct consumer of `IHttpContextAccessor` in the Application layer (the first is tracked under issue #1716), reinforcing a coupling the architecture explicitly wants to remove.
- The handler picks up an unrelated responsibility (HTTP identity extraction) on top of its core orchestration of manufacture-order state transitions, violating SRP.

This refactor aligns the handler with the established pattern, removes the duplicate logic, and restores a consistent audit trail without changing externally observable behaviour for users whose `Name` claim is present.

## Functional Requirements

### FR-1: Replace IHttpContextAccessor dependency with ICurrentUserService
The handler's constructor must no longer take `IHttpContextAccessor`. It must take `ICurrentUserService` instead and store it in a `_currentUserService` field. The private `GetCurrentUserName()` method must be removed.

**Acceptance criteria:**
- `UpdateManufactureOrderStatusHandler` has no `IHttpContextAccessor` field, parameter, or `using` import.
- `UpdateManufactureOrderStatusHandler` has a single `ICurrentUserService _currentUserService` field assigned from a constructor parameter.
- The class no longer defines `GetCurrentUserName()`.
- The project compiles (`dotnet build`) with no new warnings.

### FR-2: Resolve current user via the shared abstraction
Every call site that previously called `GetCurrentUserName()` must instead call `_currentUserService.GetCurrentUser().GetDisplayName()` using the existing `CurrentUserExtensions.GetDisplayName()` helper.

**Acceptance criteria:**
- All previous call sites of `GetCurrentUserName()` (the two sites identified in the brief, plus any others discovered during refactor) now read the user name through `ICurrentUserService` + `GetDisplayName()`.
- A grep for `_httpContextAccessor` in the handler returns no matches.
- The audit field `StateChangedByUser` (and any other user-stamp fields written by this handler) is populated from `GetDisplayName()`.

### FR-3: Preserve audit-trail behaviour for fully populated identities
For requests where `Identity.Name` is present (the case that already works today), the persisted user value must remain identical to the pre-refactor value, so existing data continues to round-trip cleanly.

**Acceptance criteria:**
- A unit/integration test exercising a state change with a `ClaimsPrincipal` whose `Identity.Name = "user@example.com"` records `"user@example.com"` in `StateChangedByUser`.
- No existing manufacture-order test assertion on user stamping needs to be loosened to pass.

### FR-4: Correctly stamp authenticated users when Name claim is absent
For requests where `Identity.Name` is null but other Entra ID identity claims are present (`preferred_username`, `upn`, `oid`, `sub`, etc.), the persisted user value must come from the fallback chain implemented in `CurrentUserService`, not the literal `"System"`.

**Acceptance criteria:**
- A unit/integration test exercising a state change with a `ClaimsPrincipal` that has `preferred_username` but no `Identity.Name` records the `preferred_username` value (or the first matching fallback claim) in `StateChangedByUser`, not `"System"`.
- The fallback semantics match `CurrentUserService.GetCurrentUser()` exactly — this refactor does not redefine them.

### FR-5: Preserve unauthenticated/system fallback
For requests with no authenticated user (e.g. background jobs that go through the same handler, or an absent `HttpContext`), the persisted user value must continue to be the sentinel string `"System"` so existing rows and downstream consumers remain consistent.

**Acceptance criteria:**
- When `ICurrentUserService.GetCurrentUser()` returns an anonymous/unauthenticated user, `GetDisplayName()` resolves to `"System"` and the audit field is stamped as `"System"`.
- If `GetDisplayName()` does not currently return `"System"` for the anonymous case, this is treated as an Open Question rather than silently introducing a different sentinel.

### FR-6: Update handler registration and tests to match the new constructor
All DI registrations, manual `new UpdateManufactureOrderStatusHandler(...)` constructions, and test doubles must be updated to supply `ICurrentUserService` in place of `IHttpContextAccessor`.

**Acceptance criteria:**
- `dotnet build` succeeds across solution.
- All existing tests that instantiate the handler compile and pass.
- No test continues to mock `IHttpContextAccessor` solely for this handler.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance change is expected. `ICurrentUserService` resolution cost is equivalent to direct `HttpContext.User` access plus a short claim lookup. The handler must not introduce additional async work or extra service-locator calls.

**Acceptance criteria:**
- Per-request latency for `UpdateManufactureOrderStatus` is unchanged within normal noise on local benchmarks (no new I/O, no new awaits, no reflection).

### NFR-2: Security
The refactor must not relax or alter the authentication/authorization model. `ICurrentUserService` must continue to read from the request's `ClaimsPrincipal` only; no new trust boundaries are introduced. The handler's existing authorization requirements (already enforced upstream by the controller/policy) remain unchanged.

**Acceptance criteria:**
- No new endpoints, claims, or permission checks are added or removed.
- Audit trail entries cannot be spoofed by clients beyond what was already possible (i.e. they still derive only from the validated access-token claims).

### NFR-3: Maintainability / Architecture
The refactor must reduce Application-layer coupling to `IHttpContextAccessor` by one consumer and bring the handler in line with the SRP boundary documented in `docs/architecture/development_guidelines.md`.

**Acceptance criteria:**
- `Anela.Heblo.Application` has one fewer file that imports `Microsoft.AspNetCore.Http.IHttpContextAccessor`.
- The handler's responsibilities are limited to manufacture-order state-transition orchestration; HTTP identity concerns no longer appear in it.

### NFR-4: Observability
Existing logs and metrics emitted by the handler must continue to include the user identifier. Where the log message previously used the locally-resolved name, it must now use the `GetDisplayName()` result.

**Acceptance criteria:**
- Any log statement that referenced the user name in the handler still emits a non-empty user identifier for authenticated requests.
- Structured log property names (e.g. `User`, `ChangedBy`) are unchanged.

## Data Model

No schema changes. Affected fields and their owners:

- `ManufactureOrder.StateChangedByUser` (string) — populated by this handler on state transitions.
- Any additional user-stamp fields written inside the same handler (e.g. state-change history rows on a related entity). The refactor preserves their column types and semantics; only the source of the string changes from `HttpContext.User.Identity.Name ?? "System"` to `ICurrentUserService.GetCurrentUser().GetDisplayName()`.

Existing rows are not migrated. Historical `"System"` rows that were caused by the bug remain `"System"` — this spec does not include a backfill.

## API / Interface Design

No public API surface change. The MediatR request, response DTOs, controller route, and HTTP contract for `UpdateManufactureOrderStatus` are untouched.

Internal interface impact:
- `UpdateManufactureOrderStatusHandler` constructor signature changes: `IHttpContextAccessor` parameter is removed and replaced by `ICurrentUserService`. Parameter ordering inside the constructor should preserve existing positions for the remaining dependencies; the swapped parameter takes the slot of the removed one.
- DI container registration of `ICurrentUserService` must already cover this handler's scope (it does — `CurrentUserService` is registered for the Application layer).

## Dependencies

- `ICurrentUserService` and its `CurrentUserService` implementation (existing).
- `CurrentUserExtensions.GetDisplayName()` (existing).
- Standard MediatR + DI infrastructure (existing).

No new NuGet packages. No new external services.

## Out of Scope

- Backfilling historical manufacture-order rows whose `StateChangedByUser` was incorrectly stamped `"System"` because of this bug.
- Refactoring the other Application-layer consumer of `IHttpContextAccessor` tracked in issue #1716 — that is a separate change.
- Changing the claim fallback order inside `CurrentUserService`. This spec uses the existing chain as-is.
- Renaming `StateChangedByUser` or any related audit columns.
- Adding new audit fields (e.g. user object id alongside display name).
- Frontend changes. The HTTP contract is unchanged, so no frontend work is required.

## Open Questions

None.

## Status: COMPLETE