# Architecture Review: Slim UpdateManufactureOrderStatusHandler

## Skip Design: true
Backend-only refactor; no UI.

## Architectural Fit Assessment
Fits the Manufacture module's pattern of pushing multi-step concerns into `Services/` (workflows, `ManufactureInventoryWriteDownService`, `ManufactureConditionsCaptureService`). Verified in code: the handler is invoked through MediatR by the controller (`ManufactureOrderController.UpdateOrderStatus`) and by both `Confirm*Workflow` classes (`TransitionToCompletedAsync`, `UpdateStatusAsync`).

The issue's primary suggestion (an `UpdateManufactureOrderStatusWorkflow` that calls the handler and then runs side effects) is rejected, for two reasons grounded in the code:
1. Conditions capture and write-down currently run BEFORE `UpdateOrderAsync`, and an exception there prevents persistence. Running them after the handler persisted would let a state change commit while the stock write-down failed (inconsistent ERP/stock state).
2. The controller and both workflows would all have to be repointed from `IMediator.Send(UpdateManufactureOrderStatusRequest)` to the new workflow, otherwise a caller silently skips the side effects. Large blast radius for a cosmetic gain.

## Proposed Architecture

### Component Overview
```
Controller / Confirm*Workflows --Mediator--> UpdateManufactureOrderStatusHandler
   deps: IManufactureOrderRepository, TimeProvider, ILogger, ICurrentUserService,
         IManufactureOrderStatusSideEffects
   1 load -> 2 CanTransitionTo -> 3 order.ChangeState
   4 order.ApplyErpDocuments(...)   (domain method, one timestamp)
   5 sideEffects.BeforePersistAsync(order, newState, userName, ct)
         -> conditions capture (guarded) + inventory write-down (Completed)
   6 repository.UpdateOrderAsync
   7 sideEffects.AfterPersistAsync(order, ct)  -> catalog refresh (safe)
```
`ManufactureOrderStatusSideEffects` (new, Application/Features/Manufacture/Services) depends on `IManufactureInventoryWriteDownService`, `IManufactureConditionsCaptureService`, `IManufactureCatalogSource`, `ILogger`.

### Key Design Decisions

#### Decision 1: Side-effects service with Before/After hooks, handler stays the single entry point
**Options considered:** (a) new workflow wrapping the handler (issue suggestion); (b) side-effects service injected into the handler; (c) do only the entity extraction.
**Chosen approach:** (b) plus the entity extraction from (c).
**Rationale:** Preserves pre-persist failure semantics and leaves all three call sites untouched. Handler drops from 7 to 5 dependencies and loses all Catalog/Inventory knowledge; the Catalog cross-module call becomes named and discoverable in the service.

#### Decision 2: ERP stamping as `ManufactureOrder.ApplyErpDocuments`
**Chosen approach:** a domain method taking `DateTime nowUtc` plus nullable code arguments (a small parameter set, no DTO from Application layer in Domain). Handler computes `now` once via `_timeProvider`. Collapses ~30 lines of if-blocks; the same single timestamp is used for all fields (previously several `GetUtcNow()` calls; differences were sub-millisecond and not relied upon).
**Rationale:** Domain must not depend on Application request types; follows `ChangeState(changedAtUtc, ...)` precedent.

#### Decision 3: Keep `ManualActionRequired`, weight fields and Note in handler
They are simple request-to-entity mapping tied to this use case; extracting more is out of scope (surgical changes).

## Implementation Guidance

### Directory / Module Structure
- New: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IManufactureOrderStatusSideEffects.cs` and `ManufactureOrderStatusSideEffects.cs`
- Edit: `UpdateManufactureOrderStatusHandler.cs`; `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs` (add method); `ManufactureModule.cs` (`AddScoped<IManufactureOrderStatusSideEffects, ManufactureOrderStatusSideEffects>()`)
- Tests: update `UpdateManufactureOrderStatusHandlerTests.cs`, `UpdateManufactureOrderStatusHandlerConditionsTests.cs`; add `Services/ManufactureOrderStatusSideEffectsTests.cs` and a domain test for `ApplyErpDocuments`.
- Docs: `docs/processes/flow-manufacture-order.md` (+ `python3 scripts/process-docs/check.py index`).

### Interfaces and Contracts
```csharp
public interface IManufactureOrderStatusSideEffects
{
    Task BeforePersistAsync(ManufactureOrder order, ManufactureOrderState newState, string changedByUser, CancellationToken ct);
    Task AfterPersistAsync(ManufactureOrder order, CancellationToken ct);
}
```
`BeforePersistAsync` retains the exact guard `newState is SemiProductManufactured or Completed && order.ConditionsReadings.All(r => r.Stage != newState)` then adds the reading; then write-down if `Completed`. Order of operations unchanged (capture before write-down). `AfterPersistAsync` calls `RefreshPlannedDataSafelyAsync(logger, order.Id, ct)`.

### Data Flow
Unchanged end-to-end; only the code location of steps 5 and 7 moves.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Reordering side effects vs persist alters failure semantics | High | Keep Before/After hooks at the same positions; test that a throwing write-down prevents `UpdateOrderAsync` |
| Single timestamp changes `*Date` values by micro-differences | Low | Acceptable; assert equality to the injected fake time in tests |
| Process-docs CI gate fails | Medium | Update `flow-manufacture-order.md` in the same PR and run the index check |
| Missed DI registration | Medium | Register in `ManufactureModule`; existing DI/integration tests catch it |

## Specification Amendments
FR-2 clarified: service exposes two hooks (before/after persist) rather than a workflow wrapping the handler. FR-4 holds because callers are unchanged.

## Prerequisites
None (no migrations, config or infrastructure).
