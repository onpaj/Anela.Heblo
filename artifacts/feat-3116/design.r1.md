# Design: Encapsulate ManufactureOrder state transition rules in the Domain entity

> **Skip Design: true** — Backend-only pure refactor. No UI/UX work. The architecture
> review (`arch-review.r1.md`) is authoritative; this document captures only the
> component/data design that the planner needs.

## Component Design

### Domain layer — `ManufactureOrder` (aggregate root)
- Add a public, side-effect-free predicate `bool CanTransitionTo(ManufactureOrderState newState)`
  that encodes the legal state transition table (currently in the Application handler).
- The transition table reproduced exactly as it exists today:
  - `Draft` → `Planned`, `Cancelled`
  - `Planned` → `Draft`, `SemiProductManufactured`, `Cancelled`, `Completed`
  - `SemiProductManufactured` → `Planned`, `Completed`, `Cancelled`
  - `Completed` → `SemiProductManufactured`, `Cancelled`, `Planned`
  - `Cancelled` → (none)
  - default → false
- The method reads the entity's own `State` as the "from" state and the argument as the "to" state.

### Application layer — `UpdateManufactureOrderStatusHandler`
- Replace the call to the private `IsValidStateTransition(oldState, request.NewState)`
  with `!order.CanTransitionTo(request.NewState)`.
- Delete the now-unused private `IsValidStateTransition` method.
- Preserve the existing error response/branching behaviour exactly (same error code,
  same message, same control flow).

### Constraint on the `State` setter
Per the architecture review: `ManufactureOrder.State` is currently assigned via object
initializers in production (`CreateManufactureOrderHandler`) and in ~12 test fixtures.
Tightening the setter is **out of scope / optional**; the primary deliverable is the
`CanTransitionTo` predicate plus the handler wiring. Do not break existing initializer
usage.

## Data Schemas
No database schema, API contract, DTO, or event payload changes. `ManufactureOrderState`
enum is unchanged. The public REST behaviour of the update-status endpoint is unchanged.
