# Specification: Backend-Driven Manufacture Order State Transitions

## Summary
The manufacture order detail page currently re-implements the order state machine in the frontend (`getStateTransitions`, `isStateTransitionBackward` in `ManufactureOrderDetail.tsx`). This feature makes the backend the single source of truth: `GetManufactureOrderResponse` exposes `availableTransitions` computed via `ManufactureOrder.CanTransitionTo`, and the frontend renders transition controls from that list.

## Background
Authoritative rules: `ManufactureOrder.CanTransitionTo` (Domain). Current graph: Draft->{Planned,Cancelled}; Planned->{Draft,SemiProductManufactured,Completed,Cancelled}; SemiProductManufactured->{Planned,Completed,Cancelled}; Completed->{SemiProductManufactured,Planned,Cancelled}; Cancelled->{}. The frontend keeps a second copy (separately for SinglePhase/MultiPhase) which can drift, showing buttons the backend rejects (`UpdateManufactureOrderStatusHandler` calls `CanTransitionTo`). Source: GitHub issue #4466 (arch-review finding).

## Functional Requirements

### FR-1: Backend returns available transitions
`GetManufactureOrderResponse` gains `AvailableTransitions` (list of `ManufactureOrderState`), populated in `GetManufactureOrderHandler` by evaluating `order.CanTransitionTo(s)` for every `ManufactureOrderState` value other than the current state.
**Acceptance criteria:**
- For each state, the list equals exactly the legal targets from `CanTransitionTo`.
- Cancelled order yields an empty list.
- Not-found response leaves the list empty (default), no exception.
- The list is ordered by enum value (deterministic).

### FR-2: Frontend renders controls from the backend list
`ManufactureOrderDetail.tsx` no longer contains per-state transition maps. `currentStateTransitions` ({next, previous}) passed to `StateTransitionControls` is derived only from `availableTransitions`.
**Acceptance criteria:**
- A button is shown only if its target is in `availableTransitions`.
- Existing behaviour is preserved: MultiPhase Draft->next Planned; Planned->next SemiProductManufactured, prev Draft; SemiProduct->next Completed, prev Planned; Completed->prev SemiProductManufactured, no next. SinglePhase skips SemiProductManufactured (Planned->next Completed; Completed->prev Planned).
- Cancelled shows no transition buttons (as before).
- Selection rule (presentation only, no legality rules): `next` = lowest-ordered available forward state excluding Cancelled; `previous` = highest-ordered available backward state; SinglePhase excludes SemiProductManufactured from candidates.
- While order is loading or list absent, no transition buttons are shown.

### FR-3: Backward-confirmation stays a display concern
`isStateTransitionBackward` may remain as a pure display ordering helper (drives the "return" confirmation dialog) but must not decide legality.

## Non-Functional Requirements
### NFR-1: Performance
Negligible: 5 in-memory predicate calls per detail request.
### NFR-2: Security
No change; the backend still enforces transitions on `UpdateManufactureOrderStatus`. The list is advisory for UI only.
### NFR-3: Compatibility
Additive API change; the OpenAPI TypeScript client regenerates on build. DTO rules: classes only, no records.

## Data Model
No DB change. `GetManufactureOrderResponse.AvailableTransitions : List<ManufactureOrderState>` (not on the shared `ManufactureOrderDto`, which is also used by list responses).

## API / Interface Design
`GET` manufacture order by id: response adds `availableTransitions: ManufactureOrderState[]`. `StateTransitionControls` props unchanged (`next`/`previous`).

## Dependencies
`ManufactureOrder.CanTransitionTo`; generated API client (`frontend/src/api/generated/api-client.ts`); existing tests `GetManufactureOrderHandlerTests`, `ManufactureOrderDetail.*.test.tsx`.

## Out of Scope
- Moving next/previous "primary action" semantics or SinglePhase skipping into the backend/domain.
- Changing the transition graph.
- Changing the `UpdateManufactureOrderStatus` handler.
- Other consumers of `ManufactureOrderState` in the frontend.

## Open Questions
None.

## Status: COMPLETE
