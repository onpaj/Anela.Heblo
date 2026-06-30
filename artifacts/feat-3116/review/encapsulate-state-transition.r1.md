# Code Review: Encapsulate ManufactureOrder state transition rules

## Summary
The implementation relocates the state-transition rules into the `ManufactureOrder` aggregate exactly as specified: a pure `CanTransitionTo` predicate reproducing the verbatim matrix, a guarded `ChangeState` throwing `ValidationException`, and an `InitializeState` seeding path, with `State`/`StateChangedAt`/`StateChangedByUser` tightened to `internal set`. The handler keeps its `InvalidOperation { oldState, newState }` early return (not a thrown path), `IsValidStateTransition` and the type-aware test mirror helpers are deleted, and tests cover the full matrix plus legal/illegal `ChangeState`.

## Review Result: PASS

### task: encapsulate-state-transition
**Status:** PASS

## Overall Notes
- `CanTransitionTo` (ManufactureOrder.cs:76-84) matches the authoritative matrix exactly: Draft→{Planned,Cancelled}; Planned→{Draft,SemiProductManufactured,Cancelled,Completed}; SemiProductManufactured→{Planned,Completed,Cancelled}; Completed→{SemiProductManufactured,Cancelled,Planned}; Cancelled→false; `_ => false`. No arm includes its own state, so all self-transitions return false. Type-agnostic — no `ManufactureType` dependency.
- `ChangeState` (lines 90-100) validates before mutating; throws `ValidationException` and leaves the entity untouched on failure.
- Handler (UpdateManufactureOrderStatusHandler.cs:53-69) captures `oldState` before mutation, pre-checks with `CanTransitionTo`, returns the same `InvalidOperation { oldState, newState }` early return (not via exception), and mutates via `ChangeState`. All side effects and response shapes unchanged. `IsValidStateTransition` is deleted.
- `internal set` applied to all three fields; `InitializeState` added as the sanctioned seeding path. No DB/DTO/error-code changes. Follows the TransportBox precedent (no state-log collection added).
- `CreateManufactureOrderHandler` seeds via `InitializeState` (line 56); `DuplicateManufactureOrderHandler` also redirected (forced by `internal set`, production file not originally named but correctly handled, no behavior change).
- Tests (ManufactureOrderStateTransitionTests.cs): full 25-cell matrix theory including the terminal Cancelled row and all self-transitions, plus `ChangeState` legal and illegal-unchanged tests. The three type-aware mirror helpers and their theories are removed (verified absent across the backend).
- Verified no residual `order.State =` assignments in production or test code; remaining `.State` references are equality comparisons only.
