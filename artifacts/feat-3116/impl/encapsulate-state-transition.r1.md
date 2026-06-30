# Implementation: encapsulate-state-transition

## What was implemented
Relocated the `ManufactureOrder` legal state-transition rules from the Application
layer into the Domain aggregate, with no change to runtime behavior or the HTTP contract.

- `State` / `StateChangedAt` / `StateChangedByUser` setters tightened to `internal set`
  so Application/Test assemblies can no longer do `order.State = x`.
- Added `InitializeState(...)` — sanctioned, unguarded seeding path used at creation time.
- Added `CanTransitionTo(...)` — pure predicate reproducing the old handler transition
  matrix verbatim (type-agnostic; all self-transitions and the `Cancelled` row return false).
- Added `ChangeState(...)` — guarded mutator that throws `ValidationException` on an illegal
  transition (validate-before-mutate; entity left unchanged on failure) and otherwise sets
  all three fields atomically.
- Rewired `UpdateManufactureOrderStatusHandler` to pre-check with `order.CanTransitionTo(...)`
  (same `InvalidOperation { oldState, newState }` early return) and mutate via
  `order.ChangeState(...)`; deleted the private `IsValidStateTransition`.
- Seeded the initial `Draft` state via `InitializeState` in `CreateManufactureOrderHandler`
  and `DuplicateManufactureOrderHandler` (the latter also assigned `State` in an initializer
  and would not compile under `internal set`).
- Rewrote `ManufactureOrderStateTransitionTests` to test the real entity; deleted the three
  type-aware private mirror helpers and their theories.
- Redirected arrange-phase `State = ...` seeding in the affected test fixtures through
  `InitializeState`. Assignments on DTOs/request objects (`ManufactureOrderDto`,
  `GetManufactureOrdersRequest`) were left untouched.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs` — `internal set`
  on the three state fields; added `using System.ComponentModel.DataAnnotations;`,
  `InitializeState`, `CanTransitionTo`, `ChangeState`.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`
  — pre-check via `CanTransitionTo`, mutate via `ChangeState`, deleted `IsValidStateTransition`.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs`
  — seed initial state via `InitializeState`.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs`
  — seed initial state via `InitializeState` (forced by `internal set`).
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderStateTransitionTests.cs`
  — full rewrite: matrix theory for `CanTransitionTo`, plus legal/illegal `ChangeState` tests.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderHandlerTests.cs`
  — fixture redirect to `InitializeState`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderScheduleHandlerTests.cs`
  — fixture + two post-construction seeds redirected to `InitializeState`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerConditionsTests.cs`
  — `CreateOrderInState` redirected to `InitializeState`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerTests.cs`
  — `CreateOrderInState` redirected to `InitializeState`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs`
  — inline + `BuildCompletedOrder` redirected to `InitializeState`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureOrderHandlerTests.cs`
  — entity fixture redirected (DTO fixture left as-is).
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureOrdersHandlerTests.cs`
  — `CreateSampleOrders` rebuilt with locals + `InitializeState` (DTO/request inits left as-is).
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs`
  — `BuildSourceOrder` redirected to `InitializeState`.

## Tests
`ManufactureOrderStateTransitionTests` now contains:
- `CanTransitionTo_ReturnsExpected` — `[Theory]` covering every cell of the matrix (all five
  rows incl. the terminal `Cancelled` row and all self-transitions).
- `ChangeState_OnIllegalTransition_ThrowsAndLeavesEntityUnchanged`.
- `ChangeState_OnLegalTransition_UpdatesAllThreeFields`.

Handler-level tests keep their existing expectations (illegal transition still asserts
`ErrorCodes.InvalidOperation`, not a thrown exception / `InternalServerError`).

## How to verify
- `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — succeeds (pre-existing
  warnings only, no errors).
- `dotnet format Anela.Heblo.sln --verify-no-changes --include <touched files>` — exit 0.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture"`
  — all green (includes the repository materialization tests that confirm EF still binds `State`
  through the `internal set`).

## Notes
- `DuplicateManufactureOrderHandler.cs` was not named in the task context but is a production
  file that assigned `State` in a `new ManufactureOrder { ... }` initializer; it had to be
  redirected through `InitializeState` to compile under `internal set`. No behavior change.
- EF materialization is unaffected: `ApplicationDbContext` exposes `ManufactureOrders` as a plain
  `DbSet` with no config class, materializing through the property accessor by convention — the
  same convention `TransportBox` uses with a non-public setter.

## PR Summary
Moves `ManufactureOrder`'s legal state-transition rules out of the Application handler and into
the domain aggregate, following the existing `TransportBox` precedent. The transition matrix is
reproduced verbatim (type-agnostic), so there is no change to runtime behavior or the HTTP
contract.

### Changes
- `ManufactureOrder` — `State`/`StateChangedAt`/`StateChangedByUser` are now `internal set`;
  added `InitializeState` (seeding), `CanTransitionTo` (pure predicate), and `ChangeState`
  (guarded mutator throwing `ValidationException` on an illegal transition).
- `UpdateManufactureOrderStatusHandler` — pre-checks with `order.CanTransitionTo(...)` (same
  `InvalidOperation { oldState, newState }` early return) and mutates via `order.ChangeState(...)`;
  the private `IsValidStateTransition` was deleted. All side effects and response shapes unchanged.
- `CreateManufactureOrderHandler` / `DuplicateManufactureOrderHandler` — seed the initial `Draft`
  state via `InitializeState`.
- `ManufactureOrderStateTransitionTests` — replaced the three type-aware private mirror helpers
  and their theories (which never exercised production code) with exhaustive matrix tests against
  the real `CanTransitionTo`, plus legal/illegal `ChangeState` tests.
- Test fixtures across the Manufacture suite — arrange-phase `State` seeding routed through
  `InitializeState`; no expected outcomes changed.

## Status
DONE
