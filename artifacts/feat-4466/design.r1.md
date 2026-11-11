# Design: Backend-Driven Manufacture Order State Transitions

## Component Design
- `GetManufactureOrderHandler`: after mapping the DTO, sets `AvailableTransitions` from `order.CanTransitionTo` over all enum values except current state, ordered by enum value.
- `stateTransitions.ts` (pure): 
  - `STATE_ORDER` display ranking: Draft 0, Planned 1, SemiProductManufactured 2, Completed 3, Cancelled 4.
  - `deriveStateTransitions(current, available, manufactureType)`: candidates = `available` minus Cancelled, minus SemiProductManufactured when SinglePhase; `next` = candidate with lowest rank above current's rank (or null); `previous` = candidate with highest rank below current's rank (or null). Undefined/empty `available` -> both null. Current Cancelled -> both null.
  - `isStateTransitionBackward(current, next)` moved here (display only).
- `ManufactureOrderDetail.tsx`: reads `availableTransitions` from the order query data, computes `currentStateTransitions` via the selector, passes unchanged to `StateTransitionControls`.

## Data Schemas
Response (additive):
```json
{ "order": { "...": "..." }, "availableTransitions": ["Planned", "Cancelled"] }
```
C#: `public List<ManufactureOrderState> AvailableTransitions { get; set; } = new();` (class, default empty).

Expected derived results (MultiPhase): Draft{avail Planned,Cancelled}->next Planned/prev null; Planned{Draft,Semi,Completed,Cancelled}->next Semi/prev Draft; Semi{Planned,Completed,Cancelled}->next Completed/prev Planned; Completed{Semi,Planned,Cancelled}->next null/prev Semi. SinglePhase: Planned->next Completed/prev Draft; Completed->prev Planned.
