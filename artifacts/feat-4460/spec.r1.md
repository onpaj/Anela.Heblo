# Specification: Slim down UpdateManufactureOrderStatusHandler (separate state transition from side effects)

## Summary
`UpdateManufactureOrderStatusHandler` (7 constructor dependencies) mixes the core state transition with ERP doc-code stamping, room-conditions capture, inventory write-down and a catalog cache refresh. This refactor separates those concerns so the handler is limited to load, validate, apply, persist and respond, without changing externally observable behaviour.

## Background
Issue #4460 (arch-review, Manufacture). The handler at `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs` is called by (a) `ManufactureOrderController.UpdateOrderStatus` (PATCH `{id}/status`), (b) `ConfirmSemiProductManufactureWorkflow`, and (c) `ConfirmProductCompletionWorkflow`, all via `IMediator.Send`. The module already extracts multi-step concerns into `Services/Workflows/*`. Current behaviour that must be preserved:
- Conditions capture (state `SemiProductManufactured` or `Completed`, only if no reading exists for that stage) and inventory write-down (`Completed`) run BEFORE `UpdateOrderAsync`; an exception aborts the call (caught, returns `InternalServerError`) and nothing is persisted.
- Catalog planned-data refresh runs AFTER persist and is failure-tolerant (`RefreshPlannedDataSafelyAsync`).
- ERP fields (`ErpOrderNumber*`, `Doc*`) and their `*Date` companions are stamped with "now" when the request supplies a non-null value.

## Functional Requirements

### FR-1: Handler limited to core transition
The handler loads the order, validates via `CanTransitionTo`, applies `ChangeState`, applies request field updates, adds the note, persists and returns the response. It must not reference inventory write-down, conditions capture or catalog services directly.
**Acceptance criteria:**
- Handler constructor has at most 5 dependencies (repository, TimeProvider, logger, current user service, one side-effects collaborator).
- Not-found, invalid-transition and exception paths return the same error codes and params as today.

### FR-2: Side effects extracted into a dedicated service
Create a service (`IManufactureOrderStatusSideEffects` or similarly named, in `Features/Manufacture/Services/`) owning conditions capture, inventory write-down (pre-persist) and catalog refresh (post-persist).
**Acceptance criteria:**
- Conditions capture and write-down still execute before persistence; a failure in either still prevents `UpdateOrderAsync` and yields `InternalServerError`.
- Catalog refresh still executes after persistence and never fails the request.
- Conditions guard (`SemiProductManufactured`/`Completed` and no existing reading for the stage) is preserved exactly.

### FR-3: ERP document stamping moved into the entity
Add a domain method on `ManufactureOrder` that stamps ERP order numbers/doc codes and their dates using a single supplied timestamp, replacing the if-guarded block in the handler (the issue's fallback suggestion).
**Acceptance criteria:**
- Null inputs leave the existing value and date untouched; non-null inputs set value and date.
- Covers `SemiProductOrderCode`, `ProductOrderCode` and the five `FlexiDoc*` fields.

### FR-4: All callers keep working with no new wiring
The controller and both workflows keep sending `UpdateManufactureOrderStatusRequest` through MediatR; request/response DTOs and the HTTP API are unchanged (no OpenAPI client regeneration impact).
**Acceptance criteria:**
- No change to `UpdateManufactureOrderStatusRequest/Response` public shape.
- Workflow tests and controller tests pass unchanged.

### FR-5: Tests and docs
**Acceptance criteria:**
- Existing `UpdateManufactureOrderStatusHandlerTests` and `...ConditionsTests` are updated for the new constructor and keep their behavioural assertions.
- New unit tests for the side-effects service and the entity stamping method.
- `docs/processes/flow-manufacture-order.md` (owns the handler folder) is updated in the same PR (or `verified_at` bumped) and `python3 scripts/process-docs/check.py index` is run.

## Non-Functional Requirements
### NFR-1: Performance
No additional DB round trips or external calls versus today.
### NFR-2: Security
No change to authorization (`FeatureAuthorize` on the controller).
### NFR-3: Compatibility
DTOs remain classes; DI registration in `ManufactureModule.cs` updated for the new service.

## Data Model
No schema or migration changes. `ManufactureOrder` gains one behaviour method; no new persisted properties.

## API / Interface Design
HTTP and MediatR contracts unchanged. New internal interface for side effects with a pre-persist and a post-persist operation.

## Dependencies
`IManufactureInventoryWriteDownService`, `IManufactureConditionsCaptureService`, `IManufactureCatalogSource` (Catalog cross-module via adapter), `ManufactureModule` DI.

## Out of Scope
- Changing transition rules, error codes or the write-down/conditions logic.
- Moving catalog refresh out of `UpdateManufactureOrderHandler`.
- Changing the two existing workflows beyond what compiling requires.
- Making side effects run post-persist (would change failure semantics).

## Open Questions
None.

## Status: COMPLETE
