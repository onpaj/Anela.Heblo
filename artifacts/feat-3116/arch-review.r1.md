# Architecture Review: Move ManufactureOrder State Transition Rules into the Domain Aggregate

## Skip Design: true

## Architectural Fit Assessment

This is a textbook Clean Architecture / DDD refactor and it fits the codebase cleanly. The
project's own guidelines explicitly call this out: `development_guidelines.md` lists "Don't create
anemic domain models — put behavior in entities" as a common pitfall, and CLAUDE.md states
"business logic belongs in the domain." Today the legal-transition matrix is a domain invariant
that lives in an Application handler (`UpdateManufactureOrderStatusHandler.IsValidStateTransition`),
and `ManufactureOrder.State` is an unguarded `public get; set;`. Moving the rule onto the aggregate
is squarely aligned with the documented architecture.

The codebase already contains the canonical reference implementation for this exact pattern:
`TransportBox` (`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs`) keeps
`State` as `private set`, mutates it only through a private `ChangeState(...)` that delegates to
`CheckState(...)`, and throws `ValidationException` on an illegal transition. The spec correctly
nominates this as the precedent to follow. The one meaningful deviation we recommend (below) is
**not** mirroring `TransportBox`'s `private set` literally, because `ManufactureOrder` is constructed
and seeded very differently from `TransportBox`.

**Verification of the brief's suggested rule vs. the actual current code:** I read the live handler
(lines 162–174). The brief's proposed `CanTransitionTo` body is an **exact** transcription of the
current `IsValidStateTransition(fromState, toState)` switch — same five `from` arms, same `to`
targets, same `_ => false` default. There is no discrepancy between the brief's fix and production
behavior. The only "discrepancy" in the area is the stale, type-aware test helpers (see below),
which never exercised this handler.

## Proposed Architecture

### Component Overview

| Component | Change | Location |
|-----------|--------|----------|
| `ManufactureOrder` | Add `CanTransitionTo` (pure predicate) + `ChangeState` (guarded mutator); reduce `State` setter visibility | `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs` |
| `UpdateManufactureOrderStatusHandler` | Replace `IsValidStateTransition` call with `order.CanTransitionTo(...)`; route mutation through `ChangeState`; delete private method | `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs` |
| `ManufactureOrderStateTransitionTests` | Delete stale private mirror helpers; test `CanTransitionTo` / `ChangeState` against the real entity | `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderStateTransitionTests.cs` |
| `CreateManufactureOrderHandler` + test fixtures | Adapt only if the setter visibility change breaks object-initializer seeding (see Decision 2) | various |

No new files, no DI changes, no DB migration, no EF configuration file (the entity is
convention-mapped — there is **no** `ManufactureOrderConfiguration` class; `ManufactureOrders` is a
plain `DbSet` on `ApplicationDbContext`).

### Key Design Decisions

#### Decision 1: `CanTransitionTo` is a pure predicate that reproduces the handler matrix exactly

**Options considered:**
- (a) Copy the handler's type-agnostic switch verbatim onto the entity (brief's proposal).
- (b) Adopt the richer `ManufactureType`-aware rules embodied in the stale test helpers.

**Chosen approach:** (a). Reproduce the current handler matrix one-for-one.

**Rationale:** This is a behavior-preserving refactor (FR-2). The type-aware helpers
(`ValidateSinglePhaseTransition` / `ValidateMultiPhaseTransition`) in the existing test file are
private copies that "mirror the private methods in `ManufactureOrderApplicationService`" — a service
that no longer drives the live path. They never executed production code. Introducing them now would
be a silent behavioral change (e.g. it would start rejecting `Planned → SemiProductManufactured` for
single-phase orders, which the live endpoint currently allows). That is explicitly out of scope.

The exact table to reproduce (verbatim from the live handler, lines 165–173):

```csharp
public bool CanTransitionTo(ManufactureOrderState newState) => State switch
{
    ManufactureOrderState.Draft => newState is ManufactureOrderState.Planned or ManufactureOrderState.Cancelled,
    ManufactureOrderState.Planned => newState is ManufactureOrderState.Draft or ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Cancelled or ManufactureOrderState.Completed,
    ManufactureOrderState.SemiProductManufactured => newState is ManufactureOrderState.Planned or ManufactureOrderState.Completed or ManufactureOrderState.Cancelled,
    ManufactureOrderState.Completed => newState is ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Cancelled or ManufactureOrderState.Planned,
    ManufactureOrderState.Cancelled => false,
    _ => false
};
```

Note: every arm omits its own state, so all self-transitions return `false` — preserve that.

#### Decision 2: Setter visibility — use `private set`, but expect to touch the seam points

**Options considered:**
- (a) `private set` (matches `TransportBox`).
- (b) `internal set`.
- (c) Leave `public set`, document `ChangeState` as the sanctioned path.

**Chosen approach:** (a) `private set` — but be aware this is **not** a free change here, unlike
`TransportBox`. `ManufactureOrder` is consistently constructed via C# object initializers that set
`State` inline. Concretely:
- Production: `CreateManufactureOrderHandler` (line ~55) does `State = ManufactureOrderState.Draft` inside `new ManufactureOrder { ... }`.
- Tests: at least 12 sites use `State = ...` in initializers or assign it directly, including
  `GetManufactureProtocolHandlerTests` (`new ManufactureOrder { ... State = ... }`),
  `GetManufactureOrdersHandlerTests`, `GetManufactureOrderHandlerTests`,
  `DuplicateManufactureOrderHandlerTests`, `UpdateManufactureOrderHandlerTests`,
  `UpdateManufactureOrderStatusHandlerConditionsTests` (`State = state`),
  `UpdateManufactureOrderStatusHandlerTests` (`State = state`), and
  `UpdateManufactureOrderScheduleHandlerTests` which does post-construction `existingOrder.State = ...`.

A `private set` breaks every one of these object-initializer assignments at compile time.

**Rationale & mitigation:** Do not let this scare the implementer into option (c); the whole point
of the task (FR-4) is to make `order.State = x` impossible from outside the Domain assembly. The
clean resolution is to provide a domain-sanctioned **initial-state** path so seeding does not need
the setter:

- Give `ManufactureOrder` an explicit way to set the *initial* `Draft` state with audit fields —
  either a small constructor/factory `ManufactureOrder.CreateDraft(...)` or an `InitializeState(state, at, by)`
  used only at creation. `CreateManufactureOrderHandler` switches to that.
- Keep `private set` so EF Core can still materialize via the backing property (EF Core sets
  private/`init` setters through its property accessor by convention — this works for `TransportBox`
  today with the same convention-based mapping, so it will work here).
- Update the test fixtures. Two acceptable tactics: (i) add a test-only builder/factory in the
  Domain test area that calls the sanctioned construction path, or (ii) where the test only needs an
  order in a given state for arrange, construct it and call `ChangeState` (with a legal predecessor)
  or the initializer factory. Prefer a single shared test helper to avoid touching ~12 files
  individually — but this is a mechanical, in-scope adaptation, not new behavior.

If, and only if, the initial-state factory proves disproportionately invasive across the seed sites,
fall back to `internal set` (option b): it still forbids `order.State = x` from the Application and
test assemblies while letting Domain-internal seeding compile. This is the spec's documented
fallback (Assumption B) and is acceptable. `public set` (option c) is the last resort and should be
accompanied by a code comment — but it is discouraged because it leaves FR-4 only partially met.

**Recommendation:** Target `private set` + a `CreateDraft`/initializer factory + one shared test
builder. Treat `internal set` as the pragmatic fallback if the factory churn is large.

#### Decision 3: `ChangeState` mutator with `ValidationException`, handler pre-checks with `CanTransitionTo`

**Options considered:** Throw `ValidationException` (TransportBox precedent) vs.
`InvalidOperationException`.

**Chosen approach:** `void ChangeState(ManufactureOrderState newState, DateTime changedAtUtc, string changedByUser)`
that throws `System.ComponentModel.DataAnnotations.ValidationException` when `!CanTransitionTo(newState)`,
and otherwise sets `State`, `StateChangedAt`, `StateChangedByUser` together. Mirror `TransportBox`
shape exactly (it sets state, timestamp, then appends to its log — `ManufactureOrder` has no state
log, so it just sets the three fields; do **not** add a state-log collection, that's out of scope).

**Rationale:** Consistency with `TransportBox` is the codebase norm. Critically, the handler must
**keep its `CanTransitionTo` pre-check and early-return** so the HTTP contract is unchanged: an
illegal transition still returns `ErrorCodes.InvalidOperation` with `{ oldState, newState }`, never
a thrown exception bubbling to the `catch` (which would wrongly produce `InternalServerError`). The
`ChangeState` throw is therefore a *defensive* second line, not the primary control flow. The
ordering in the handler becomes:

```csharp
var oldState = order.State;
if (!order.CanTransitionTo(request.NewState))
    return new UpdateManufactureOrderStatusResponse(ErrorCodes.InvalidOperation,
        new Dictionary<string, string> { { "oldState", oldState.ToString() }, { "newState", request.NewState.ToString() } });

var currentUserName = _currentUserService.GetCurrentUser().GetDisplayName();
order.ChangeState(request.NewState, _timeProvider.GetUtcNow().DateTime, currentUserName);
// ... remaining side effects unchanged (ManualActionRequired, ERP codes, Flexi docs, notes, conditions, inventory)
```

This preserves the exact ordering of audit-field writes today (lines 69–71) and keeps
`currentUserName` available for the downstream `WriteDownInventoryAsync` and note creation.

## Implementation Guidance

### Directory / Module Structure

No structural change. Edits land in three existing files (Domain entity, Application handler, Domain
test) plus mechanical fixture adaptation. No new module, no DI registration, no
`PersistenceModule`/`ManufactureModule` change.

### Interfaces and Contracts

New public surface on `ManufactureOrder` (Domain assembly):
- `public bool CanTransitionTo(ManufactureOrderState newState)` — pure, reads `this.State` + arg only.
- `public void ChangeState(ManufactureOrderState newState, DateTime changedAtUtc, string changedByUser)` — guarded; throws `ValidationException`; sets the three fields atomically; sets nothing when it throws (validate first, mutate second).
- A sanctioned initial-state path (e.g. `public static ManufactureOrder CreateDraft(...)` or `public void InitializeState(...)`) so creation does not depend on a public `State` setter.
- `State { get; private set; }` (or `internal set` fallback).

Removed Application surface:
- `UpdateManufactureOrderStatusHandler.IsValidStateTransition(...)` — deleted.

HTTP/MediatR contract (`UpdateManufactureOrderStatusRequest` → `UpdateManufactureOrderStatusResponse`)
is unchanged: same DTOs, same `ErrorCodes.InvalidOperation` / `ResourceNotFound` / `InternalServerError`,
same success payload (`OldState`, `NewState`, `StateChangedAt`, `StateChangedByUser`).

### Data Flow

1. Controller → MediatR → `UpdateManufactureOrderStatusHandler.Handle`.
2. Load order; if null → `ResourceNotFound`.
3. Capture `oldState`. Pre-check `order.CanTransitionTo(request.NewState)`; if false → `InvalidOperation { oldState, newState }`.
4. Resolve current user via `ICurrentUserService` (per ADR-005, inside the handler — unchanged).
5. `order.ChangeState(newState, now, user)` sets state + audit fields.
6. Remaining side effects (ManualActionRequired, ERP order numbers, weight, Flexi doc codes, note,
   conditions reading at `SemiProductManufactured`/`Completed`, inventory write-down at `Completed`)
   run exactly as today.
7. `UpdateOrderAsync` persists; success response returned.

EF read paths (`GetOrdersAsync` filtering on `x.State`, `UpcomingProductionTile`, repository queries)
are read-only and unaffected by the setter visibility change.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `private set` breaks ~13 object-initializer/assignment sites (`State = ...`) including production `CreateManufactureOrderHandler` | High (compile-break) | Add a sanctioned `CreateDraft`/`InitializeState` path; add one shared test builder; or fall back to `internal set` (spec Assumption B). This is the bulk of the work — plan for it. |
| EF Core can't materialize `State` with a non-public setter | Low | Convention-based mapping (no config file) materializes through the property accessor; `TransportBox` already uses `private set` with the same convention and works. Verify via existing `ManufactureOrderRepositoryTests`. |
| Accidentally importing the type-aware rules from the stale test helpers, silently changing behavior | Medium | Decision 1 — reproduce the handler matrix verbatim; delete the `ValidateSinglePhase`/`ValidateMultiPhase`/3-arg helpers entirely. Cross-check every cell against handler lines 165–173. |
| `ChangeState` exception escaping to the handler `catch` and turning a 400-style `InvalidOperation` into `InternalServerError` | Medium | Keep the `CanTransitionTo` pre-check + early return; `ChangeState` throw is defensive only. Existing `UpdateManufactureOrderStatusHandlerTests` invalid-transition cases must still assert `InvalidOperation`. |
| `ChangeState` partially mutates when transition is illegal | Low | Validate before assigning any field; unit-test that the entity is unchanged after a thrown `ValidationException`. |
| `UpdateManufactureOrderScheduleHandlerTests` assigns `existingOrder.State` post-construction (lines 85, 107) | Low | These are arrange-phase setups; route through the test builder / `ChangeState`. Not production writers. |

## Specification Amendments

- **FR-4 / Assumption B (setter visibility):** Strengthen the guidance. Because `ManufactureOrder`
  (unlike `TransportBox`) is created and seeded via object initializers that set `State` inline, a
  `private set` is a real, non-trivial change requiring a sanctioned initial-state construction path
  and test-fixture updates. The spec should explicitly call for adding a `CreateDraft`/`InitializeState`
  domain method as part of FR-4 rather than presenting `private set` as nearly free. `internal set`
  remains the acceptable fallback.
- **Background note:** The spec references an EF "entity type configuration" for `ManufactureOrder`
  (Dependencies section). There is **no** such configuration class — the entity is convention-mapped
  via the `ManufactureOrders` `DbSet` on `ApplicationDbContext`. The persistence verification step
  should target `ManufactureOrderRepositoryTests`, not a config file.
- **FR-5:** Confirm deletion (not "keep, marked stale") of the three private mirror helpers in
  `ManufactureOrderStateTransitionTests`. Keeping them — even annotated — invites future confusion
  about which rules are authoritative. Recommend full removal; if the type-aware intent must be
  preserved, capture it as a backlog item / Open Question, not as dead test code.

## Prerequisites

None blocking. All inputs verified against live source:
- Handler matrix (lines 162–174) confirmed; brief's `CanTransitionTo` matches it exactly.
- `ManufactureOrderState` enum confirmed: `Draft=1, Planned=2, SemiProductManufactured=4, Completed=5, Cancelled=6`.
- `TransportBox` precedent confirmed (`private set` + `ChangeState` + `CheckState` + `ValidationException`).
- Direct/initializer `State` writers enumerated (1 production: `CreateManufactureOrderHandler`; 1 production mutator: `UpdateManufactureOrderStatusHandler`; ~12 test fixtures).
- No EF configuration class; convention-based mapping.

Validation gate before completion: `dotnet build` + `dotnet format`; all touched tests
(`ManufactureOrderStateTransitionTests`, `UpdateManufactureOrderStatusHandlerTests`,
`UpdateManufactureOrderStatusHandlerConditionsTests`, `CreateManufactureOrderHandlerTests`,
`UpdateManufactureOrderScheduleHandlerTests`, `ManufactureOrderRepositoryTests`) green.
