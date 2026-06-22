# Specification: Move ManufactureOrder State Transition Rules into the Domain Aggregate

## Summary
The `ManufactureOrder` aggregate currently exposes `State` as an unguarded public setter, and the only enforcement of legal state transitions lives in an Application-layer handler (`UpdateManufactureOrderStatusHandler.IsValidStateTransition`). This violates the project's "business logic belongs in the domain" rule and leaves every other code path free to assign arbitrary states. This is a backend-only domain refactoring that relocates the transition rules onto the `ManufactureOrder` entity as a first-class, unit-testable domain invariant, and rewires the handler (and any other writers) to go through it.

## Background
`ManufactureOrder` (`backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs`) is an EF-mapped aggregate with `public ManufactureOrderState State { get; set; }` plus companion audit fields `StateChangedAt` and `StateChangedByUser`. The legal-transition matrix is encoded in a private method on `UpdateManufactureOrderStatusHandler` (`backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`, lines 162–174).

Problems with the current arrangement:
- The transition rule is a domain invariant but lives in the Application layer, contradicting `CLAUDE.md` and `docs/architecture/development_guidelines.md` ("Don't create anemic domain models — put behavior in entities").
- Any handler or ad-hoc repository update can do `order.State = anything;` with no validation.
- The rule cannot be unit-tested against the entity in isolation; only against the handler.

There is an established sibling pattern in the same codebase: `TransportBox` (`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs`) keeps `State` with a `private set`, mutates it only through a private `ChangeState(...)` that calls `CheckState(...)`, throws `ValidationException` on an illegal transition, and appends to an internal state log. This refactor should align `ManufactureOrder` with that pattern as far as is practical without expanding scope.

### Important pre-existing discrepancy (must be resolved)
The transition matrix in the handler (`IsValidStateTransition`) is **type-agnostic** — it does not distinguish `ManufactureType.SinglePhase` from `ManufactureType.MultiPhase`. However, the existing test file `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderStateTransitionTests.cs` tests a **type-aware** rule set (separate `ValidateSinglePhaseTransition` / `ValidateMultiPhaseTransition` helpers, plus an `IsValidStateTransition(current, target, manufactureType)`). These test helpers are private copies that "mirror" a `ManufactureOrderApplicationService` that no longer drives the live path — they do not exercise production code. The brief's suggested `CanTransitionTo` mirrors the **handler** (type-agnostic). This spec adopts the handler's current behavior as the authoritative production rule to preserve existing runtime behavior, and treats the type-aware test as documentation of intended-but-not-implemented richer rules (see FR-2 and Open Questions).

The relevant enums:
- `ManufactureOrderState` (`ManufactureOrderState.cs`): `Draft = 1, Planned = 2, SemiProductManufactured = 4, Completed = 5, Cancelled = 6`.
- `ManufactureType` (`ManufactureType.cs`): `MultiPhase = 0` (default), `SinglePhase = 1`.

## Functional Requirements

### FR-1: Add a transition-validation method to the `ManufactureOrder` aggregate
Add a public, side-effect-free method `bool CanTransitionTo(ManufactureOrderState newState)` to `ManufactureOrder`. It returns `true` iff a transition from the current `State` to `newState` is legal. The rule set must be exactly equivalent to the current handler logic to preserve runtime behavior:

| From | Allowed To |
|------|-----------|
| `Draft` | `Planned`, `Cancelled` |
| `Planned` | `Draft`, `SemiProductManufactured`, `Cancelled`, `Completed` |
| `SemiProductManufactured` | `Planned`, `Completed`, `Cancelled` |
| `Completed` | `SemiProductManufactured`, `Cancelled`, `Planned` |
| `Cancelled` | (none) |
| any other | (none) |

**Acceptance criteria:**
- `CanTransitionTo` is defined on `ManufactureOrder` and is `public`.
- The method is pure: it reads `this.State` and the argument only; it mutates nothing.
- For every (from, to) pair, the method returns exactly the same boolean as the current `IsValidStateTransition(from, to)`.
- A self-transition (e.g. `Planned` → `Planned`) returns `false` for every state, matching the current handler matrix (no state lists itself).
- The method is callable from a unit test that constructs a `ManufactureOrder`, sets `State`, and asserts the result — no handler, repository, or DI required.

### FR-2: Preserve current runtime transition behavior (no behavioral change to the API)
This is a refactor, not a rules change. The set of transitions the `UpdateManufactureOrderStatus` endpoint accepts/rejects must be identical before and after.

**Acceptance criteria:**
- The `ManufactureType`-aware rules present only in the old test helpers are NOT introduced into production behavior as part of this task (any such change is out of scope and tracked in Open Questions).
- An invalid transition still produces `ErrorCodes.InvalidOperation` with the `oldState`/`newState` parameters dictionary, exactly as today (see FR-4).
- A valid transition still updates `State`, `StateChangedAt`, `StateChangedByUser`, and performs all existing side effects (conditions reading capture, inventory write-down, ERP/Flexi doc fields, notes) unchanged.

### FR-3: Rewire `UpdateManufactureOrderStatusHandler` to use the aggregate method
Replace the call `IsValidStateTransition(oldState, request.NewState)` with `order.CanTransitionTo(request.NewState)` and delete the private `IsValidStateTransition` method from the handler.

**Acceptance criteria:**
- The handler no longer declares `IsValidStateTransition`.
- The guard reads `if (!order.CanTransitionTo(request.NewState)) { return InvalidOperation ... }`, preserving the existing early-return error shape.
- `oldState` is still captured before mutation for use in the error and success responses.
- No other behavior in `Handle(...)` changes.

### FR-4: Protect `State` against unguarded external mutation
Strengthen the invariant so callers cannot bypass the rule by assigning `State` directly. Provide a single mutation entry point on the aggregate that applies the audit fields together with the state change.

Add a method, e.g. `void ChangeState(ManufactureOrderState newState, DateTime changedAtUtc, string changedByUser)`, that:
- Throws (e.g. `InvalidOperationException` or `System.ComponentModel.DataAnnotations.ValidationException`, matching the `TransportBox` precedent which uses `ValidationException`) when `!CanTransitionTo(newState)`.
- On success sets `State`, `StateChangedAt`, and `StateChangedByUser` atomically.

Reduce the visibility of the `State` setter from `public set` toward the most restrictive level that keeps EF Core and existing read paths working. Preferred target is `private set` (matching `TransportBox`). If EF Core materialization or existing serialization/mapping requires it, fall back to `internal set` or `protected set` — but the public mutation path for application code must be `ChangeState`/`CanTransitionTo`, not a raw setter.

**Acceptance criteria:**
- Application code can no longer perform `order.State = x;` from outside the Domain assembly (compile-time enforced by the reduced setter visibility), OR — if EF constraints force a looser setter — `ChangeState` is the documented and used mutation path and a code-level comment explains the constraint.
- `ChangeState` throws on an illegal transition and does not modify any field when it throws.
- The handler at line ~68–71 uses `ChangeState` (or equivalent) so `StateChangedAt`/`StateChangedByUser` are still set from `_timeProvider` and `_currentUserService` as today.
- All other writers of `order.State` are updated to the new path. Confirmed current direct writers: `UpdateManufactureOrderStatusHandler` (line 69). Test fixtures that set `State` directly (e.g. `UpdateManufactureOrderScheduleHandlerTests`, `CreateManufactureOrderHandlerTests`) are updated to use a domain-appropriate construction path; these are arrange-phase test setups, not production writers.
- The EF Core mapping for `ManufactureOrder` (`backend/src/Anela.Heblo.Persistence/Manufacture/`) continues to read/write `State` correctly after the visibility change. Verified by the existing persistence/repository tests passing.

### FR-5: Relocate and extend the entity-level transition tests
Migrate the transition coverage so it exercises the real domain method. The existing `ManufactureOrderStateTransitionTests` uses private copies of removed logic; replace those with tests that call `ManufactureOrder.CanTransitionTo` directly.

**Acceptance criteria:**
- A test class (may reuse the existing `ManufactureOrderStateTransitionTests` file, located in the Domain test area) drives `CanTransitionTo` via `[Theory]`/`[InlineData]` covering every cell of the FR-1 matrix, including the always-`false` rows for `Cancelled` and self-transitions.
- The private mirror helpers (`ValidateSinglePhaseTransition`, `ValidateMultiPhaseTransition`, the 3-arg `IsValidStateTransition`) are removed unless a separate decision (Open Questions) keeps them documenting future type-aware rules; if kept, they must be clearly marked as not reflecting current production behavior.
- A test asserts `ChangeState` throws on an illegal transition and leaves the entity unchanged, and succeeds (updating all three fields) on a legal one.
- The handler-level test(s) for `UpdateManufactureOrderStatus` (if any) continue to pass without modification to their expected outcomes.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. `CanTransitionTo` is an O(1) `switch` over an enum; it replaces an equivalent O(1) switch. No new allocations, I/O, or queries.

### NFR-2: Security / Integrity
The change strengthens an integrity invariant (illegal state transitions become impossible to commit through the application). No authentication/authorization surface changes. No new data is exposed. Error responses retain the existing shape and do not leak additional internal detail.

### NFR-3: Maintainability & Architecture conformance
- Domain logic moves into the Domain assembly (`Anela.Heblo.Domain`), conforming to Clean Architecture and `development_guidelines.md`.
- The pattern aligns with the existing `TransportBox` aggregate (private state setter + guarded mutation method).
- `dotnet build` and `dotnet format` must pass; all touched tests must pass.

### NFR-4: Backward compatibility
- No database migration is required (the underlying column and mapping are unchanged; only C# property visibility changes).
- The OpenAPI/HTTP contract of `UpdateManufactureOrderStatus` is unchanged (same request/response DTOs, same error codes).

## Data Model
Entity affected: `ManufactureOrder` (Domain aggregate, EF-mapped).

Relevant fields (unchanged in shape, only `State` setter visibility changes):
- `State : ManufactureOrderState` — current lifecycle state.
- `StateChangedAt : DateTime` — UTC timestamp of last transition.
- `StateChangedByUser : string` — display name of the user who performed the transition.
- `ManufactureType : ManufactureType` — `MultiPhase` (default) / `SinglePhase`; relevant only to the deferred type-aware rules discussion, not to FR-1.

Enums (unchanged):
- `ManufactureOrderState`: `Draft(1)`, `Planned(2)`, `SemiProductManufactured(4)`, `Completed(5)`, `Cancelled(6)`.
- `ManufactureType`: `MultiPhase(0)`, `SinglePhase(1)`.

No new entities, columns, indexes, or relationships are introduced.

## API / Interface Design

### New domain surface on `ManufactureOrder`
- `public bool CanTransitionTo(ManufactureOrderState newState)` — pure predicate (FR-1).
- `public void ChangeState(ManufactureOrderState newState, DateTime changedAtUtc, string changedByUser)` — guarded mutator; throws on illegal transition, sets state + audit fields atomically (FR-4).
- `State` setter visibility reduced to `private set` (or the minimum that EF Core permits).

### Removed Application surface
- `UpdateManufactureOrderStatusHandler.IsValidStateTransition(...)` — deleted (FR-3).

### HTTP endpoint (behavior preserved, not redesigned)
`UpdateManufactureOrderStatus` (MediatR request `UpdateManufactureOrderStatusRequest` → `UpdateManufactureOrderStatusResponse`):
- Illegal transition → `UpdateManufactureOrderStatusResponse` with `ErrorCodes.InvalidOperation` and `{ oldState, newState }`.
- Order not found → `ErrorCodes.ResourceNotFound`.
- Unexpected exception → `ErrorCodes.InternalServerError`.
- Success → `{ OldState, NewState, StateChangedAt, StateChangedByUser }` plus all existing side effects.

No new events are emitted.

## Dependencies
- `Anela.Heblo.Domain` (entity, enums) — primary change site.
- `Anela.Heblo.Application` — `UpdateManufactureOrderStatusHandler` rewire.
- `Anela.Heblo.Persistence` — verify EF Core mapping for `ManufactureOrder.State` still binds after setter visibility change (`backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs` and the entity type configuration).
- `Anela.Heblo.Tests` — relocate/extend transition tests; fix any arrange-phase fixtures that assign `State` directly.
- No external services, NuGet packages, or migrations.

## Out of Scope
- Introducing `ManufactureType`-aware (single-phase vs multi-phase) transition rules into production. The current production rule is type-agnostic; changing it is a behavioral change tracked in Open Questions, not part of this refactor.
- Adding an internal state-change audit log/collection to `ManufactureOrder` analogous to `TransportBox.StateLog` (the order already records `StateChangedAt`/`StateChangedByUser` and a separate `ManufactureOrderNote` trail; a structured log is a larger change).
- Frontend changes of any kind.
- Refactoring other aggregates or other Manufacture use cases beyond what is needed to compile and pass tests.
- Adding new error codes or changing response DTOs.

## Open Questions
None blocking. Documented assumptions (chosen to keep this a behavior-preserving refactor):

- **Assumption A (rule fidelity):** `CanTransitionTo` mirrors the current handler matrix exactly (type-agnostic), per the brief. The richer type-aware logic embedded in the existing test helpers is treated as not-yet-implemented intent and is explicitly deferred. If the product owner instead wants the type-aware rules to become authoritative, that is a follow-up behavioral change.
- **Assumption B (setter visibility):** Target `private set` for `State` to match `TransportBox`. If EF Core materialization or existing read/mapping code breaks, fall back to the least-permissive visibility that compiles (`internal`/`protected`), with `ChangeState` remaining the sanctioned mutation path. The final choice is an implementation detail validated by the persistence tests.
- **Assumption C (exception type):** `ChangeState` throws `ValidationException` to match the `TransportBox` precedent. If the team prefers `InvalidOperationException` for domain invariants, either is acceptable as long as the handler's external error contract (`ErrorCodes.InvalidOperation`) is preserved — the handler should pre-check with `CanTransitionTo` so the exception path is defensive, not the primary control flow.

## Status: COMPLETE
