# Design: Extract Transport Box State-Transition Side Effects from ChangeTransportBoxStateHandler

## Component Design

### `ChangeTransportBoxStateHandler` (modified)
- **Responsibility:** orchestration only — fetch box, assign box code/location, resolve
  `TransportBoxTransition`, evaluate its `Condition`, resolve and invoke the matching
  `ITransportBoxTransitionSideEffect`, execute `transition.ChangeStateAsync`, invoke
  `ITransportBoxInventoryRestorer` on the Opened→New rollback path, persist via
  `ITransportBoxRepository`, build the response, map domain exceptions to error codes.
- **Constructor dependencies (after refactor):** `ITransportBoxRepository`, `IMediator`,
  `ILogger<ChangeTransportBoxStateHandler>`, `ICurrentUserService`, `TimeProvider`,
  `IEnumerable<ITransportBoxTransitionSideEffect>`, `ITransportBoxInventoryRestorer`.
  (Drops direct `IInventoryReservationService` and `ILogisticsStockOperationService`
  dependencies — those move exclusively to the two collaborators below.)
- **Dispatch:** `_sideEffects.FirstOrDefault(s => s.Supports(box.State, request.NewState))`,
  replacing the removed `CallBackMap` dictionary.

### `ITransportBoxTransitionSideEffect` (new interface) + 4 implementations
- **Responsibility:** encapsulate one state-transition's side effect. Returns `null` to let
  the handler continue the transition, or a populated `ChangeTransportBoxStateResponse` to
  short-circuit with a failure result — identical contract to the private methods being
  replaced.
- `NewToOpenedSideEffect` — `Supports(New, Opened)`. Validates `BoxCode` presence, checks
  code-uniqueness via `ITransportBoxRepository.IsBoxCodeActiveAsync`, closes stale `Stocked`
  boxes sharing the code. Depends on `ITransportBoxRepository`, `ICurrentUserService`,
  `TimeProvider`.
- `OpenToReserveSideEffect` — `Supports(Opened, Reserve)`. Validates `request.Location` is
  non-empty. No injected dependencies.
- `OpenToQuarantineSideEffect` — `Supports(Opened, Quarantine)`. No-op today (kept explicit
  for symmetry/extensibility per spec FR-1). No injected dependencies.
- `ReceivedSideEffect` — `Supports(from, Received)` for `from` in `{InTransit, Reserve,
  Quarantine}`. Aggregates `box.Items` by `ProductCode`, stages one `StockUpOperation` per
  product via `ILogisticsStockOperationService.StageOperationAsync`, logs per-product debug
  and a summary info line — identical log content/levels to today's `HandleReceived`. Depends
  on `ILogisticsStockOperationService`, `ILogger<ReceivedSideEffect>`.

### `ITransportBoxInventoryRestorer` / `TransportBoxInventoryRestorer` (new)
- **Responsibility:** body of today's `RestoreInventoryForItemsAsync` — for each item with a
  non-null `SourceInventoryId`, call `IInventoryReservationService.RestoreAsync`. Invoked
  directly by the handler on the Opened→New rollback path (not part of the
  `ITransportBoxTransitionSideEffect` dispatch — see arch-review Decision 3). Depends on
  `IInventoryReservationService`.

### `LogisticsModule` (modified)
- Adds `AddTransient` registrations for the four `ITransportBoxTransitionSideEffect`
  implementations and for `ITransportBoxInventoryRestorer`, alongside the existing
  `ITransportBoxCompletionService` registration.

## Data Schemas

No schema changes. `ChangeTransportBoxStateRequest` / `ChangeTransportBoxStateResponse`
(the MediatR request/response contract, also the API-facing DTOs) are byte-for-byte
unchanged — every field, error code, and `Params` key currently returned continues to be
returned identically after the refactor. No database schema, migration, or event payload is
introduced or altered.
